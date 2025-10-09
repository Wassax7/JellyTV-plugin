using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyTV.Api.Models;
using Jellyfin.Plugin.JellyTV.Configuration;
using Jellyfin.Plugin.JellyTV.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTV.Api;

/// <summary>
/// JellyTV API controller.
/// </summary>
[ApiController]
[Route("Plugins/{pluginGuid}/JellyTV")]
public class JellyTVController : ControllerBase
{
    private readonly JellyTVPushService _pushService;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVController"/> class.
    /// </summary>
    /// <param name="pushService">The push service used to deliver notifications.</param>
    public JellyTVController(JellyTVPushService pushService)
    {
        _pushService = pushService;
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
            userId,
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
    public ActionResult SetPreferences([FromRoute] string pluginGuid, [FromBody] PreferencesRequest request)
    {
        if (!Guid.TryParse(pluginGuid, out var routeGuid) || routeGuid != Plugin.Instance?.Id)
        {
            return NotFound();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("userId is required");
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

        var result = JellyTVUserStore.UpsertToken(request.UserId, request.Token);

        // Send a confirmation push only if the token is newly registered
        if (result.IsNewToken)
        {
            await _pushService.SendRegistrationConfirmationAsync(request.UserId, request.Token).ConfigureAwait(false);
        }

        return Ok(new { status = "ok", userId = request.UserId, tokens = result.User.Tokens.ToArray() });
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
}
