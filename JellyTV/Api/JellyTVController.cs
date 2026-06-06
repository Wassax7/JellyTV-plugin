using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyTV.Api.Models;
using Jellyfin.Plugin.JellyTV.Configuration;
using Jellyfin.Plugin.JellyTV.Services;
using Jellyfin.Plugin.JellyTV.Utilities;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.Api;

/// <summary>
/// JellyTV API controller.
/// </summary>
[ApiController]
[Route("Plugins/{pluginGuid}/JellyTV")]
public class JellyTVController : ControllerBase
{
    private const string DefaultBannerIconType = "info";
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(60);
    private static readonly Guid[] LegacyPluginIds =
    [
        Guid.Parse("eb5d7894-8eef-4b36-aa6f-5d124e828ce1"),
        Guid.Parse("9da8e914-0355-49a1-9851-f94b6f468d59")
    ];

    private readonly JellyTVPushService _pushService;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IUserManager _userManager;
    private readonly RateLimitService _rateLimitService;
    private readonly ILogger<JellyTVController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVController"/> class.
    /// </summary>
    /// <param name="pushService">The push service used to deliver notifications.</param>
    /// <param name="authorizationContext">The authorization context.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="rateLimitService">The rate limit service.</param>
    /// <param name="logger">The logger.</param>
    public JellyTVController(
        JellyTVPushService pushService,
        IAuthorizationContext authorizationContext,
        IUserManager userManager,
        RateLimitService rateLimitService,
        ILogger<JellyTVController> logger)
    {
        _pushService = pushService;
        _authorizationContext = authorizationContext;
        _userManager = userManager;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    private static string CleanApnsToken(string token)
    {
        var cleaned = new string((token ?? string.Empty).Where(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray());
        return cleaned.ToLowerInvariant();
    }

    private static bool IsValidApnsToken(string token)
        => !string.IsNullOrWhiteSpace(token) && token.Length == 64 && token.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private static string NormalizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        return Guid.TryParse(userId, out var guid)
            ? guid.ToString("N")
            : userId.Trim().ToLowerInvariant();
    }

    private static bool TryParseRouteGuid(string pluginGuid, out Guid routeGuid)
        => Guid.TryParse(pluginGuid, out routeGuid);

    private static bool IsCurrentPluginRoute(Guid routeGuid)
        => routeGuid == Plugin.Instance?.Id;

    private static bool IsAppPluginRoute(Guid routeGuid)
        => IsCurrentPluginRoute(routeGuid) || LegacyPluginIds.Contains(routeGuid);

    /// <summary>
    /// Gets app-facing JellyTV settings for the authenticated user.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <returns>HTTP 200 with app-facing settings.</returns>
    [HttpGet("app-settings")]
    public async Task<ActionResult<AppSettingsResponse>> GetAppSettings([FromRoute] string pluginGuid, [FromQuery] string userId)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId is required");
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null)
        {
            return Unauthorized("Authentication required");
        }

        var normalizedUserId = NormalizeUserId(userId);
        if (!CanAccessUser(auth, normalizedUserId))
        {
            return Forbid("Cannot read another user's settings");
        }

        return Ok(BuildAppSettingsResponse(normalizedUserId));
    }

    /// <summary>
    /// Gets administrator-managed JellyTV settings and registered user summaries.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <returns>HTTP 200 with administrator settings.</returns>
    [HttpGet("admin/settings")]
    public async Task<ActionResult<AdminSettingsDto>> GetAdminSettings([FromRoute] string pluginGuid)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null)
        {
            return Unauthorized("Authentication required");
        }

        if (!IsAdminOrApiKey(auth))
        {
            return Forbid("Admin access required");
        }

        return Ok(BuildAdminSettingsResponse());
    }

    /// <summary>
    /// Updates administrator-managed JellyTV settings.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The settings payload.</param>
    /// <returns>HTTP 200 with updated administrator settings.</returns>
    [HttpPut("admin/settings")]
    public async Task<ActionResult<AdminSettingsDto>> UpdateAdminSettings([FromRoute] string pluginGuid, [FromBody] AdminSettingsDto request)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (request == null)
        {
            return BadRequest("settings payload is required");
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null)
        {
            return Unauthorized("Authentication required");
        }

        if (!IsAdminOrApiKey(auth))
        {
            return Forbid("Admin access required");
        }

        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(500, new { error = "Plugin is not initialized" });
        }

        var config = plugin.Configuration;
        var (isValidUrl, normalizedUrl, urlError) = UrlValidator.ValidateUrl(request.SeerrBaseUrl);
        if (!isValidUrl)
        {
            return BadRequest(new { error = urlError ?? "Invalid Seerr URL" });
        }

        config.SeerrBaseUrl = normalizedUrl ?? string.Empty;

        if (request.Notifications != null)
        {
            config.ForwardItemAdded = request.Notifications.ForwardItemAdded;
            config.ForwardPlaybackStart = request.Notifications.ForwardPlaybackStart;
            config.ForwardPlaybackStop = request.Notifications.ForwardPlaybackStop;
        }

        if (request.Banner != null)
        {
            var bannerResult = ApplyBannerSettings(config, request.Banner);
            if (!string.IsNullOrWhiteSpace(bannerResult))
            {
                return BadRequest(new { error = bannerResult });
            }
        }

        if (request.ArrWebhooks != null)
        {
            var secret = request.ArrWebhooks.Secret?.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                return BadRequest(new { error = "arrWebhooks.secret is required" });
            }

            config.ArrWebhookSecret = secret;
        }

        plugin.UpdateConfiguration(config);
        return Ok(BuildAdminSettingsResponse());
    }

    /// <summary>
    /// Gets per-user delivery preferences for JellyTV events along with admin settings.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <returns>HTTP 200 with the preferences JSON including admin settings.</returns>
    [HttpGet("preferences/{userId}")]
    public async Task<ActionResult> GetPreferences([FromRoute] string pluginGuid, [FromRoute] string userId)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId is required");
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null && IsCurrentPluginRoute(routeGuid))
        {
            return Unauthorized("Authentication required");
        }

        if (auth != null && !CanAccessUser(auth, NormalizeUserId(userId)))
        {
            return Forbid("Cannot read another user's preferences");
        }

        var config = Plugin.Instance?.Configuration;
        var prefs = JellyTVUserStore.GetPreferences(userId);
        return Ok(new
        {
            UserId = userId,
            AdminSettings = new
            {
                ForwardItemAdded = config?.ForwardItemAdded ?? true,
                ForwardPlaybackStart = config?.ForwardPlaybackStart ?? false,
                ForwardPlaybackStop = config?.ForwardPlaybackStop ?? false
            },
            ForwardItemAdded = prefs?.ForwardItemAdded ?? (config?.ForwardItemAdded ?? true),
            ForwardPlaybackStart = prefs?.ForwardPlaybackStart,
            ForwardPlaybackStop = prefs?.ForwardPlaybackStop
        });
    }

    /// <summary>
    /// Sets per-user delivery preferences for JellyTV events.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The preferences payload.</param>
    /// <returns>HTTP 200 on success.</returns>
    [HttpPost("preferences")]
    public async Task<ActionResult> SetPreferences([FromRoute] string pluginGuid, [FromBody] PreferencesRequest request)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("userId is required");
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null)
        {
            return Unauthorized("Authentication required");
        }

        var requestedUserId = NormalizeUserId(request.UserId);
        var callerUserId = NormalizeUserId(auth.UserId.ToString());

        if (!CanAccessUser(auth, requestedUserId))
        {
            return Forbid("Cannot modify another user's preferences");
        }

        var rateLimitKey = $"preferences:{callerUserId}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 10, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many requests", retryAfter });
        }

        var prefs = new JellyTVUserPreferences
        {
            ForwardItemAdded = request.ForwardItemAdded,
            ForwardPlaybackStart = request.ForwardPlaybackStart,
            ForwardPlaybackStop = request.ForwardPlaybackStop
        };

        JellyTVUserStore.SetPreferences(request.UserId, prefs);
        return Ok(new { status = "ok" });
    }

    /// <summary>
    /// Registers a device token for a Jellyfin user.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The registration payload.</param>
    /// <returns>HTTP 200 on success.</returns>
    [HttpPost("register")]
    public async Task<ActionResult> Register([FromRoute] string pluginGuid, [FromBody] RegisterRequest request)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("userId and token are required");
        }

        var cleanedToken = CleanApnsToken(request.Token);
        if (!IsValidApnsToken(cleanedToken))
        {
            return BadRequest("Invalid APNs token format. Expected 64 hexadecimal characters.");
        }

        var normalizedUserId = NormalizeUserId(request.UserId);
        if (IsCurrentPluginRoute(routeGuid))
        {
            var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
            if (auth == null)
            {
                return Unauthorized("Authentication required");
            }

            if (!CanAccessUser(auth, normalizedUserId))
            {
                return Forbid("Cannot register a token for another user");
            }
        }

        var rateLimitKey = $"register:{normalizedUserId}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 5, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many registration requests", retryAfter });
        }

        var result = JellyTVUserStore.UpsertToken(request.UserId, cleanedToken);

        return Ok(new { status = "ok", userId = request.UserId, tokens = result.User.Tokens.ToArray() });
    }

    /// <summary>
    /// Unregisters a device token for a Jellyfin user.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The unregistration payload.</param>
    /// <returns>HTTP 200 on success.</returns>
    [HttpPost("unregister")]
    public ActionResult Unregister([FromRoute] string pluginGuid, [FromBody] UnregisterRequest request)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("token is required");
        }

        var tokenHash = request.Token.GetHashCode(StringComparison.Ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rateLimitKey = $"unregister:{tokenHash}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 5, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many requests", retryAfter });
        }

        var removed = JellyTVUserStore.RemoveToken(request.Token);

        return Ok(new { status = "ok", removed });
    }

    /// <summary>
    /// Returns all registered users from the persistent store.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <returns>HTTP 200 with the list of user IDs.</returns>
    [HttpGet("users")]
    public async Task<ActionResult> GetUsers([FromRoute] string pluginGuid)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
        if (auth == null)
        {
            return Unauthorized("Authentication required");
        }

        if (!IsAdminOrApiKey(auth))
        {
            return Forbid("Admin access required");
        }

        var users = JellyTVUserStore.Load();
        return Ok(users.Select(u => new { u.UserId }));
    }

    /// <summary>
    /// Removes a registered user and all their device tokens.
    /// Note: This endpoint is only accessible from the plugin config page, which is admin-only in Jellyfin.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The delete user request containing userId.</param>
    /// <returns>HTTP 200 on success, 404 if user not found.</returns>
    [HttpPost("users/delete")]
    public async Task<ActionResult> DeleteUser([FromRoute] string pluginGuid, [FromBody] Models.DeleteUserRequest request)
    {
        try
        {
            if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
            {
                return NotFound();
            }

            var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
            if (auth == null)
            {
                return Unauthorized("Authentication required");
            }

            if (!IsAdminOrApiKey(auth))
            {
                return Forbid("Admin access required");
            }

            var userId = request?.UserId;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new { error = "userId is required" });
            }

            var removed = JellyTVUserStore.RemoveUser(userId);
            if (!removed)
            {
                return NotFound(new { error = "User not found in registered users list" });
            }

            return Ok(new { status = "ok", userId });
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to delete JellyTV user registration");
            return StatusCode(500, new { error = "Internal error" });
        }
    }

    /// <summary>
    /// Returns the configured Seerr base URL.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <returns>HTTP 200 with { baseUrl }.</returns>
    [HttpGet("seerr")]
    [HttpGet("jellyseerr")]
    public ActionResult GetSeerrBaseUrl([FromRoute] string pluginGuid)
    {
        if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
        {
            return NotFound();
        }

        var raw = Plugin.Instance?.Configuration?.SeerrBaseUrl ?? string.Empty;
        var baseUrl = (raw ?? string.Empty).Trim();
        return Ok(new { baseUrl });
    }

    /// <summary>
    /// Sends a broadcast notification to all registered users.
    /// Note: This endpoint is only accessible from the plugin config page, which is admin-only in Jellyfin.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="request">The broadcast request containing the message.</param>
    /// <returns>HTTP 200 on success.</returns>
    [HttpPost("broadcast")]
    public async Task<ActionResult> SendBroadcast([FromRoute] string pluginGuid, [FromBody] Models.BroadcastRequest request)
    {
        try
        {
            if (!TryParseRouteGuid(pluginGuid, out var routeGuid) || !IsAppPluginRoute(routeGuid))
            {
                return NotFound();
            }

            var auth = await GetAuthenticatedAuthorizationAsync().ConfigureAwait(false);
            if (auth == null)
            {
                return Unauthorized("Authentication required");
            }

            if (!IsAdminOrApiKey(auth))
            {
                return Forbid("Admin access required");
            }

            var message = request?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                return BadRequest(new { error = "Message is required" });
            }

            const int MaxBodyLength = 4000;
            if (message.Length > MaxBodyLength)
            {
                message = message.Substring(0, MaxBodyLength);
            }

            var allUsers = JellyTVUserStore.Load().Select(u => u.UserId);
            await _pushService.SendCustomAsync("JellyTV", message, allUsers).ConfigureAwait(false);

            return Ok(new { status = "ok" });
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to send JellyTV broadcast notification");
            return StatusCode(500, new { error = "Internal error" });
        }
    }

    private static AppSettingsResponse BuildAppSettingsResponse(string userId)
    {
        var config = Plugin.Instance?.Configuration;
        var adminDefaults = BuildNotificationSettings(config);
        var userPreferences = BuildUserPreferenceSettings(JellyTVUserStore.GetPreferences(userId));

        return new AppSettingsResponse
        {
            SeerrBaseUrl = (config?.SeerrBaseUrl ?? string.Empty).Trim(),
            Banner = BuildActiveBanner(config),
            Notifications = new AppNotificationSettingsDto
            {
                AdminDefaults = adminDefaults,
                UserPreferences = userPreferences,
                Effective = BuildEffectiveNotificationSettings(adminDefaults, userPreferences)
            }
        };
    }

    private AdminSettingsDto BuildAdminSettingsResponse()
    {
        var config = Plugin.Instance?.Configuration;
        var adminDefaults = BuildNotificationSettings(config);

        return new AdminSettingsDto
        {
            SeerrBaseUrl = (config?.SeerrBaseUrl ?? string.Empty).Trim(),
            Banner = BuildStoredBanner(config),
            ArrWebhooks = BuildArrWebhookSettings(config),
            Notifications = adminDefaults,
            RegisteredUsers = JellyTVUserStore.Load()
                .Select(user =>
                {
                    var preferences = BuildUserPreferenceSettings(JellyTVUserStore.GetPreferences(user.UserId));
                    var profile = Guid.TryParse(user.UserId, out var userGuid) ? _userManager.GetUserById(userGuid) : null;
                    return new AdminRegisteredUserDto
                    {
                        UserId = user.UserId,
                        Name = profile?.Username,
                        IsDeleted = profile == null,
                        Preferences = preferences,
                        Effective = BuildEffectiveNotificationSettings(adminDefaults, preferences)
                    };
                })
                .ToList()
        };
    }

    private static ArrWebhookSettingsDto BuildArrWebhookSettings(PluginConfiguration? config)
    {
        const string path = "/JellyTV/notifications";
        var secret = config?.ArrWebhookSecret?.Trim() ?? string.Empty;
        return new ArrWebhookSettingsDto
        {
            Secret = secret,
            Path = path,
            RelativeUrl = string.IsNullOrWhiteSpace(secret)
                ? path
                : path + "?token=" + Uri.EscapeDataString(secret)
        };
    }

    private static NotificationSettingsDto BuildNotificationSettings(PluginConfiguration? config)
        => new NotificationSettingsDto
        {
            ForwardItemAdded = config?.ForwardItemAdded ?? true,
            ForwardPlaybackStart = config?.ForwardPlaybackStart ?? false,
            ForwardPlaybackStop = config?.ForwardPlaybackStop ?? false
        };

    private static UserPreferenceSettingsDto BuildUserPreferenceSettings(JellyTVUserPreferences? preferences)
        => new UserPreferenceSettingsDto
        {
            ForwardItemAdded = preferences?.ForwardItemAdded,
            ForwardPlaybackStart = preferences?.ForwardPlaybackStart,
            ForwardPlaybackStop = preferences?.ForwardPlaybackStop
        };

    private static NotificationSettingsDto BuildEffectiveNotificationSettings(
        NotificationSettingsDto adminDefaults,
        UserPreferenceSettingsDto userPreferences)
        => new NotificationSettingsDto
        {
            ForwardItemAdded = adminDefaults.ForwardItemAdded && (userPreferences.ForwardItemAdded ?? true),
            ForwardPlaybackStart = adminDefaults.ForwardPlaybackStart && (userPreferences.ForwardPlaybackStart ?? true),
            ForwardPlaybackStop = adminDefaults.ForwardPlaybackStop && (userPreferences.ForwardPlaybackStop ?? true)
        };

    private static BannerSettingsDto BuildStoredBanner(PluginConfiguration? config)
        => new BannerSettingsDto
        {
            Enabled = config?.BannerEnabled ?? false,
            IconType = NormalizeBannerIconType(config?.BannerIconType) ?? DefaultBannerIconType,
            Message = config?.BannerMessage ?? string.Empty,
            ExpiresAtUtc = string.IsNullOrWhiteSpace(config?.BannerExpiresAtUtc) ? null : config!.BannerExpiresAtUtc
        };

    private static BannerSettingsDto? BuildActiveBanner(PluginConfiguration? config)
    {
        if (config?.BannerEnabled != true || string.IsNullOrWhiteSpace(config.BannerMessage))
        {
            return null;
        }

        var expiresAtUtc = config.BannerExpiresAtUtc;
        if (!string.IsNullOrWhiteSpace(expiresAtUtc))
        {
            if (!TryReadUtcTimestamp(expiresAtUtc, out var expiresAt))
            {
                return null;
            }

            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                return null;
            }
        }

        return new BannerSettingsDto
        {
            Enabled = true,
            IconType = NormalizeBannerIconType(config.BannerIconType) ?? DefaultBannerIconType,
            Message = config.BannerMessage.Trim(),
            ExpiresAtUtc = string.IsNullOrWhiteSpace(expiresAtUtc) ? null : NormalizeUtcTimestamp(expiresAtUtc!)
        };
    }

    private static string? ApplyBannerSettings(PluginConfiguration config, BannerSettingsDto banner)
    {
        if (!banner.Enabled)
        {
            config.BannerEnabled = false;
            config.BannerIconType = DefaultBannerIconType;
            config.BannerMessage = string.Empty;
            config.BannerExpiresAtUtc = null;
            return null;
        }

        var message = banner.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Banner message is required when banner is enabled";
        }

        const int MaxBannerMessageLength = 4000;
        if (message.Length > MaxBannerMessageLength)
        {
            message = message.Substring(0, MaxBannerMessageLength);
        }

        string? expiresAtUtc = null;
        if (!string.IsNullOrWhiteSpace(banner.ExpiresAtUtc))
        {
            if (!HasUtcDesignator(banner.ExpiresAtUtc!) || !TryReadUtcTimestamp(banner.ExpiresAtUtc!, out var expiresAt))
            {
                return "expiresAtUtc must be an ISO-8601 UTC timestamp";
            }

            expiresAtUtc = FormatUtcTimestamp(expiresAt);
        }

        var iconType = NormalizeBannerIconType(banner.IconType);
        if (iconType == null && !string.IsNullOrWhiteSpace(banner.IconType))
        {
            return "iconType must be one of: info, warning, alert, success";
        }

        config.BannerEnabled = true;
        config.BannerIconType = iconType ?? DefaultBannerIconType;
        config.BannerMessage = message;
        config.BannerExpiresAtUtc = expiresAtUtc;
        return null;
    }

    private static string? NormalizeBannerIconType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "info" => "info",
            "warning" => "warning",
            "alert" => "alert",
            "success" => "success",
            _ => null
        };
    }

    private static bool HasUtcDesignator(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0
            && (trimmed[^1] == 'Z'
            || trimmed[^1] == 'z'
            || trimmed.EndsWith("+00:00", StringComparison.Ordinal)
            || trimmed.EndsWith("-00:00", StringComparison.Ordinal));
    }

    private static bool TryReadUtcTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
        {
            timestamp = timestamp.ToUniversalTime();
            return true;
        }

        timestamp = default;
        return false;
    }

    private static string NormalizeUtcTimestamp(string value)
        => TryReadUtcTimestamp(value, out var timestamp) ? FormatUtcTimestamp(timestamp) : value.Trim();

    private static string FormatUtcTimestamp(DateTimeOffset timestamp)
        => timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<AuthorizationInfo?> GetAuthenticatedAuthorizationAsync()
    {
        try
        {
            var auth = await _authorizationContext.GetAuthorizationInfo(Request.HttpContext).ConfigureAwait(false);
            if (auth == null || !auth.HasToken || !auth.IsAuthenticated)
            {
                return null;
            }

            return auth;
        }
        catch (AuthenticationException)
        {
            return null;
        }
    }

    private bool CanAccessUser(AuthorizationInfo auth, string userId)
    {
        if (IsAdminOrApiKey(auth))
        {
            return true;
        }

        if (auth.UserId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return string.Equals(NormalizeUserId(auth.UserId.ToString()), userId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAdminOrApiKey(AuthorizationInfo auth)
    {
        if (auth.UserId == Guid.Empty && auth.HasToken && auth.IsAuthenticated)
        {
            return true;
        }

        var user = auth.UserId != Guid.Empty ? _userManager.GetUserById(auth.UserId) : null;
        return user?.HasPermission(PermissionKind.IsAdministrator) ?? false;
    }
}
