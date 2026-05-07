using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyTV.Api.Models;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.Api;

/// <summary>
/// Controller to receive custom notification payloads and forward them via JellyTV push.
/// </summary>
[ApiController]
[Route("JellyTV")]
public sealed class JellyTVNotificationsController : ControllerBase
{
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions NotificationJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private readonly JellyTVPushService _pushService;
    private readonly ArrWebhookNotificationFormatter _arrFormatter;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly RateLimitService _rateLimitService;
    private readonly ILogger<JellyTVNotificationsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVNotificationsController"/> class.
    /// </summary>
    /// <param name="pushService">The push service.</param>
    /// <param name="arrFormatter">The native *arr webhook formatter.</param>
    /// <param name="userManager">The user manager to resolve usernames.</param>
    /// <param name="authorizationContext">The authorization context to validate access tokens.</param>
    /// <param name="rateLimitService">The rate limit service.</param>
    /// <param name="logger">The logger.</param>
    public JellyTVNotificationsController(
        JellyTVPushService pushService,
        ArrWebhookNotificationFormatter arrFormatter,
        IUserManager userManager,
        IAuthorizationContext authorizationContext,
        RateLimitService rateLimitService,
        ILogger<JellyTVNotificationsController> logger)
    {
        _pushService = pushService;
        _arrFormatter = arrFormatter;
        _userManager = userManager;
        _authorizationContext = authorizationContext;
        _rateLimitService = rateLimitService;
        _logger = logger;
    }

    /// <summary>
    /// Accepts an array of notification entries and forwards them to devices.
    /// Requires authentication via 'X-Emby-Token' header or 'Authorization: MediaBrowser Token="..."' header.
    /// </summary>
    /// <param name="payload">Array payload of notifications, or a native *arr webhook object.</param>
    /// <returns>HTTP 200 with a summary result.</returns>
    [HttpPost("notifications")]
    public async Task<ActionResult> PostNotifications([FromBody] JsonElement payload)
    {
        if (IsArrWebhookPayload(payload))
        {
            return await PostArrWebhookNotification(payload).ConfigureAwait(false);
        }

        var requests = ReadNotificationRequests(payload);
        if (requests == null || requests.Count == 0)
        {
            return BadRequest("Payload must be a non-empty notification array or a native *arr webhook object");
        }

        // Check for authentication token in either X-Emby-Token or Authorization header
        var hasXEmbyToken = Request.Headers.ContainsKey("X-Emby-Token");
        var hasAuthHeader = Request.Headers.ContainsKey("Authorization");

        if (!hasXEmbyToken && !hasAuthHeader)
        {
            return Unauthorized("Missing authentication header. Provide either 'X-Emby-Token' or 'Authorization: MediaBrowser Token=\"...\"'");
        }

        AuthorizationInfo? authorization;
        try
        {
            authorization = await _authorizationContext.GetAuthorizationInfo(Request.HttpContext).ConfigureAwait(false);
        }
        catch (AuthenticationException)
        {
            return Unauthorized("Invalid authentication token");
        }

        if (authorization == null || !authorization.HasToken || !authorization.IsAuthenticated)
        {
            return Unauthorized("Invalid or expired authentication token");
        }

        var isApiKey = authorization.UserId == Guid.Empty && authorization.HasToken && authorization.IsAuthenticated;

        if (!isApiKey)
        {
            var adminUser = _userManager.GetUserById(authorization.UserId);
            if (adminUser == null || !adminUser.HasPermission(PermissionKind.IsAdministrator))
            {
                return Forbid("Admin access required to send notifications");
            }
        }

        var rateLimitKey = $"notifications:{authorization.UserId:N}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 20, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many notification requests", retryAfter });
        }

        const int MaxTitleLength = 200;
        const int MaxBodyLength = 4000;

        int sent = 0;
        int skipped = 0;
        foreach (var req in requests)
        {
            var title = (req.Title ?? string.Empty).Trim();
            var body = (req.Body ?? string.Empty).Trim();
            var usernames = ReadUsernames(req.Username)
                .Select(static u => u.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (title.Length > MaxTitleLength)
            {
                title = title.Substring(0, MaxTitleLength);
            }

            if (body.Length > MaxBodyLength)
            {
                body = body.Substring(0, MaxBodyLength);
            }

            IEnumerable<string> targets;
            if (usernames.Length == 0)
            {
                targets = JellyTVUserStore.Load().Select(u => u.UserId);
            }
            else
            {
                var targetIds = new List<string>();
                foreach (var username in usernames)
                {
                    var user = _userManager.GetUserByName(username);
                    if (user == null && Guid.TryParse(username, out var userId))
                    {
                        user = _userManager.GetUserById(userId);
                    }

                    if (user == null)
                    {
                        skipped++;
                        continue;
                    }

                    targetIds.Add(user.Id.ToString("N"));
                }

                if (targetIds.Count == 0)
                {
                    continue;
                }

                targets = targetIds;
            }

            await _pushService.SendCustomAsync(title, body, targets).ConfigureAwait(false);
            sent++;
        }

        return Ok(new { status = "ok", processed = sent, skipped });
    }

    private async Task<ActionResult> PostArrWebhookNotification(JsonElement payload)
    {
        if (!IsArrWebhookAuthorized())
        {
            return Unauthorized("Invalid or missing JellyTV webhook secret");
        }

        var rateLimitKey = $"arr-notifications:{HttpContext.Connection.RemoteIpAddress}";
        if (!_rateLimitService.TryAcquire(rateLimitKey, 30, RateLimitWindow))
        {
            var retryAfter = _rateLimitService.GetRetryAfterSeconds(rateLimitKey, RateLimitWindow);
            Response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(429, new { error = "Too many webhook requests", retryAfter });
        }

        var notification = _arrFormatter.Format(payload);
        if (notification == null)
        {
            return BadRequest("Payload must be a native *arr webhook JSON object with eventType");
        }

        const int MaxTitleLength = 200;
        const int MaxBodyLength = 4000;
        var title = Truncate(notification.Title, MaxTitleLength);
        var body = Truncate(notification.Body, MaxBodyLength);
        var allUsers = JellyTVUserStore.Load().Select(static u => u.UserId);

        _logger.LogInformation("Forwarding native *arr webhook event {EventType} through /JellyTV/notifications.", notification.EventType);
        await _pushService.SendCustomAsync(title, body, allUsers).ConfigureAwait(false);

        return Ok(new { status = "ok", eventType = notification.EventType });
    }

    private static bool IsArrWebhookPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, "eventType", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString());
            }
        }

        return false;
    }

    private static System.Collections.ObjectModel.Collection<NotificationRequest>? ReadNotificationRequests(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<System.Collections.ObjectModel.Collection<NotificationRequest>>(payload.GetRawText(), NotificationJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool IsArrWebhookAuthorized()
    {
        var config = Plugin.Instance?.Configuration;
        if (string.IsNullOrWhiteSpace(config?.ArrWebhookSecret))
        {
            return false;
        }

        var expected = config.ArrWebhookSecret.Trim();
        return ReadCandidateSecrets().Any(candidate => SecretEquals(expected, candidate));
    }

    private string[] ReadCandidateSecrets()
    {
        var values = new List<string>();
        if (Request.Query.TryGetValue("token", out var queryToken))
        {
            values.AddRange(queryToken.Where(static value => !string.IsNullOrWhiteSpace(value))!);
        }

        if (Request.Headers.TryGetValue("X-JellyTV-Webhook-Key", out var headerToken))
        {
            values.AddRange(headerToken.Where(static value => !string.IsNullOrWhiteSpace(value))!);
        }

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            foreach (var value in authHeader)
            {
                values.Add(ReadAuthorizationSecret(value));
            }
        }

        return values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray()!;
    }

    private static string ReadAuthorizationSecret(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return string.Empty;
        }

        const string bearerPrefix = "Bearer ";
        if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return authorization.Substring(bearerPrefix.Length).Trim();
        }

        const string basicPrefix = "Basic ";
        if (!authorization.StartsWith(basicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        try
        {
            var raw = Convert.FromBase64String(authorization.Substring(basicPrefix.Length).Trim());
            var userPass = Encoding.UTF8.GetString(raw);
            var separator = userPass.IndexOf(':', StringComparison.Ordinal);
            return separator >= 0 ? userPass.Substring(separator + 1) : userPass;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static bool SecretEquals(string expected, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate.Trim());
        return expectedBytes.Length == candidateBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }

    private static List<string> ReadUsernames(JsonElement username)
    {
        var values = new List<string>();
        switch (username.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return values;
            case JsonValueKind.String:
                var value = username.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }

                return values;
            case JsonValueKind.Array:
                foreach (var item in username.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }

                return values;
            default:
                return values;
        }
    }
}
