using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyTV.Api.Models;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTV.Api;

/// <summary>
/// Controller to receive custom notification payloads and forward them via JellyTV push.
/// </summary>
[ApiController]
[Route("JellyTV")]
public sealed class JellyTVNotificationsController : ControllerBase
{
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(60);

    private readonly JellyTVPushService _pushService;
    private readonly IUserManager _userManager;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly RateLimitService _rateLimitService;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVNotificationsController"/> class.
    /// </summary>
    /// <param name="pushService">The push service.</param>
    /// <param name="userManager">The user manager to resolve usernames.</param>
    /// <param name="authorizationContext">The authorization context to validate access tokens.</param>
    /// <param name="rateLimitService">The rate limit service.</param>
    public JellyTVNotificationsController(
        JellyTVPushService pushService,
        IUserManager userManager,
        IAuthorizationContext authorizationContext,
        RateLimitService rateLimitService)
    {
        _pushService = pushService;
        _userManager = userManager;
        _authorizationContext = authorizationContext;
        _rateLimitService = rateLimitService;
    }

    /// <summary>
    /// Accepts an array of notification entries and forwards them to devices.
    /// Requires authentication via 'X-Emby-Token' header or 'Authorization: MediaBrowser Token="..."' header.
    /// </summary>
    /// <param name="requests">Array payload of notifications.</param>
    /// <returns>HTTP 200 with a summary result.</returns>
    [HttpPost("notifications")]
    public async Task<ActionResult> PostNotifications([FromBody] System.Collections.ObjectModel.Collection<NotificationRequest> requests)
    {
        // Check for authentication token in either X-Emby-Token or Authorization header
        var hasXEmbyToken = Request.Headers.ContainsKey("X-Emby-Token");
        var hasAuthHeader = Request.Headers.ContainsKey("Authorization");

        if (!hasXEmbyToken && !hasAuthHeader)
        {
            return Unauthorized("Missing authentication header. Provide either 'X-Emby-Token' or 'Authorization: MediaBrowser Token=\"...\"'");
        }

        if (requests == null || requests.Count == 0)
        {
            return BadRequest("Payload must be a non-empty array");
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

        var adminUser = authorization.UserId != Guid.Empty ? _userManager.GetUserById(authorization.UserId) : null;
        if (adminUser == null || !adminUser.HasPermission(PermissionKind.IsAdministrator))
        {
            return Forbid("Admin access required to send notifications");
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
            var username = (req.Username ?? string.Empty).Trim();

            if (title.Length > MaxTitleLength)
            {
                title = title.Substring(0, MaxTitleLength);
            }

            if (body.Length > MaxBodyLength)
            {
                body = body.Substring(0, MaxBodyLength);
            }

            IEnumerable<string> targets;
            if (string.IsNullOrWhiteSpace(username))
            {
                targets = JellyTVUserStore.Load().Select(u => u.UserId);
            }
            else
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

                targets = new[] { user.Id.ToString("N") };
            }

            await _pushService.SendCustomAsync(title, body, targets).ConfigureAwait(false);
            sent++;
        }

        return Ok(new { status = "ok", processed = sent, skipped });
    }
}
