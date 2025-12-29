using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyTV.Api.Models;
using Jellyfin.Plugin.JellyTV.Configuration;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTV.Api;

/// <summary>
/// JellyTV API controller.
/// </summary>
[ApiController]
[Route("Plugins/{pluginGuid}/JellyTV")]
public class JellyTVController : ControllerBase
{
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(60);

    private readonly JellyTVPushService _pushService;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly IUserManager _userManager;
    private readonly RateLimitService _rateLimitService;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVController"/> class.
    /// </summary>
    /// <param name="pushService">The push service used to deliver notifications.</param>
    /// <param name="authorizationContext">The authorization context.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="rateLimitService">The rate limit service.</param>
    public JellyTVController(
        JellyTVPushService pushService,
        IAuthorizationContext authorizationContext,
        IUserManager userManager,
        RateLimitService rateLimitService)
    {
        _pushService = pushService;
        _authorizationContext = authorizationContext;
        _userManager = userManager;
        _rateLimitService = rateLimitService;
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

    /// <summary>
    /// Gets per-user delivery preferences for JellyTV events.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <returns>HTTP 200 with the preferences JSON.</returns>
    [HttpGet("preferences/{userId}")]
    public ActionResult GetPreferences([FromRoute] string pluginGuid, [FromRoute] string userId)
    {
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId is required");
        }

        var prefs = JellyTVUserStore.GetPreferences(userId);
        return Ok(new
        {
            UserId = userId,
            ForwardItemAdded = prefs?.ForwardItemAdded,
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
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
        {
            return NotFound();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("userId is required");
        }

        var auth = await _authorizationContext.GetAuthorizationInfo(Request.HttpContext).ConfigureAwait(false);
        var requestedUserId = NormalizeUserId(request.UserId);
        var callerUserId = NormalizeUserId(auth.UserId.ToString());

        var callerUser = auth.UserId != Guid.Empty ? _userManager.GetUserById(auth.UserId) : null;
        var isAdmin = callerUser?.HasPermission(PermissionKind.IsAdministrator) ?? false;
        if (!string.Equals(requestedUserId, callerUserId, StringComparison.OrdinalIgnoreCase) && !isAdmin)
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
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
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
        var rateLimitKey = $"register:{normalizedUserId}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 5, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many registration requests", retryAfter });
        }

        var result = JellyTVUserStore.UpsertToken(request.UserId, cleanedToken);

        if (result.IsNewToken && Plugin.Instance?.Configuration?.SendRegistrationConfirmation == true)
        {
            await _pushService.SendRegistrationConfirmationAsync(request.UserId, cleanedToken).ConfigureAwait(false);
        }

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
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
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
    public ActionResult GetUsers([FromRoute] string pluginGuid)
    {
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
        {
            return NotFound();
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
    public ActionResult DeleteUser([FromRoute] string pluginGuid, [FromBody] Models.DeleteUserRequest request)
    {
        try
        {
            if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
            {
                return NotFound();
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
            return StatusCode(500, new { error = "Internal error: " + ex.Message });
        }
    }

    /// <summary>
    /// Returns the configured Jellyseerr base URL.
    /// </summary>
    /// <param name="pluginGuid">The plugin guid from the route.</param>
    /// <returns>HTTP 200 with { baseUrl }.</returns>
    [HttpGet("jellyseerr")]
    public ActionResult GetJellyseerrBaseUrl([FromRoute] string pluginGuid)
    {
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
        {
            return NotFound();
        }

        var raw = Plugin.Instance?.Configuration?.JellyseerrBaseUrl ?? string.Empty;
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
            if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
            {
                return NotFound();
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
            return StatusCode(500, new { error = "Internal error: " + ex.Message });
        }
    }
}
