using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyTV.Localization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Service responsible for building and sending JellyTV push notifications via the relay.
/// </summary>
public sealed class JellyTVPushService
{
    private const string RelayUrl = "https://push.jellytv.app/api/push";
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ILogger<JellyTVPushService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVPushService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JellyTVPushService(ILogger<JellyTVPushService> logger)
    {
        _logger = logger;
    }

    private static bool IsValidApnsToken(string token)
        => !string.IsNullOrWhiteSpace(token) && token.Length == 64 && token.All(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private static string CleanApnsToken(string token)
    {
        var cleaned = new string((token ?? string.Empty).Where(c =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')).ToArray());
        return cleaned.ToLowerInvariant();
    }

    /// <summary>
    /// Sends a push notification for a Jellyfin event to the specified users' devices.
    /// </summary>
    /// <param name="eventName">The event name (e.g., PlaybackStart, PlaybackStop, ItemAdded).</param>
    /// <param name="itemId">The related item identifier (optional).</param>
    /// <param name="userIds">The Jellyfin user IDs to target.</param>
    /// <param name="itemName">The related item name (optional).</param>
    /// <param name="userName">The user name associated with the event (optional).</param>
    /// <returns>A task representing the async operation.</returns>
    public async Task SendEventAsync(string eventName, string? itemId, IEnumerable<string> userIds, string? itemName = null, string? userName = null)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return;
        }

        var deviceTokens = JellyTVUserStore.GetTokensForUsers(userIds)
            .Select(CleanApnsToken)
            .Where(IsValidApnsToken)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (deviceTokens.Count == 0)
        {
            _logger.LogInformation("No devices to push for event {Event} (item={Item}, users={UsersCount})", eventName, itemName ?? string.Empty, (userIds ?? Array.Empty<string>()).Count());
            return; // nothing to push
        }

        const string title = "JellyTV";
        var body = GetBodyForEvent(eventName, itemName, userName);
        _logger.LogInformation("Pushing {Event} to {DeviceCount} device(s). item={Item} user={User}", eventName, deviceTokens.Count, itemName ?? string.Empty, userName ?? string.Empty);
        await PostToRelayManyAsync(deviceTokens, title, body).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a custom notification with explicit title/body to the specified users' devices.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="body">The notification body.</param>
    /// <param name="userIds">The Jellyfin user IDs to target.</param>
    /// <returns>A task representing the async operation.</returns>
    public async Task SendCustomAsync(string title, string body, IEnumerable<string> userIds)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return;
        }

        var deviceTokens = JellyTVUserStore.GetTokensForUsers(userIds)
            .Select(CleanApnsToken)
            .Where(IsValidApnsToken)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (deviceTokens.Count == 0)
        {
            _logger.LogInformation("No devices to push for custom notification (users={UsersCount})", (userIds ?? Array.Empty<string>()).Count());
            return;
        }

        _logger.LogInformation("Pushing custom notification to {DeviceCount} device(s). title={Title}", deviceTokens.Count, title ?? string.Empty);
        await PostToRelayManyAsync(deviceTokens, title ?? "JellyTV", body ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a one-time confirmation push after registering a new device token.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="token">The raw device token.</param>
    /// <returns>A task representing the async operation.</returns>
    public Task SendRegistrationConfirmationAsync(string userId, string token)
    {
        const string title = "JellyTV";
        var body = Localizer.T("RegistrationBody");
        var cleaned = CleanApnsToken(token);
        if (!IsValidApnsToken(cleaned))
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Sending registration confirmation to user {UserId}", userId);
        return PostToRelaySingleAsync(cleaned, title, body);
    }

    private async Task PostToRelaySingleAsync(string deviceToken, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return;
        }

        var payloadObj = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["body"] = body,
            ["deviceToken"] = deviceToken
        };

        var invalid = await PostJsonAsync(payloadObj).ConfigureAwait(false);
        if (invalid.Count > 0)
        {
            foreach (var tok in invalid)
            {
                var removed = JellyTVUserStore.RemoveToken(tok);
                if (removed > 0)
                {
                    _logger.LogInformation("Pruned invalid token from store (single): {TokenMasked}", MaskToken(tok));
                }
            }
        }
    }

    private async Task PostToRelayManyAsync(List<string> deviceTokens, string title, string body)
    {
        if (deviceTokens == null || deviceTokens.Count == 0)
        {
            return;
        }

        var payloadObj = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["body"] = body,
            ["deviceTokens"] = deviceTokens
        };

        var invalid = await PostJsonAsync(payloadObj).ConfigureAwait(false);
        if (invalid.Count > 0)
        {
            foreach (var tok in invalid)
            {
                var removed = JellyTVUserStore.RemoveToken(tok);
                if (removed > 0)
                {
                    _logger.LogInformation("Pruned invalid token from store: {TokenMasked}", MaskToken(tok));
                }
            }
        }
    }

    private static string GetBodyForEvent(string eventName, string? itemName, string? userName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return Localizer.T("Notification");
        }

        switch (eventName)
        {
            case "PlaybackStart":
                if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(itemName))
                {
                    return Localizer.Format("PlaybackStartedByUser", new Dictionary<string, string> { ["User"] = userName!, ["Item"] = itemName! });
                }

                return string.IsNullOrWhiteSpace(itemName) ? Localizer.T("PlaybackStarted") : Localizer.Format("PlaybackStartedItem", new Dictionary<string, string> { ["Item"] = itemName! });

            case "PlaybackStop":
                if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(itemName))
                {
                    return Localizer.Format("PlaybackStoppedByUser", new Dictionary<string, string> { ["User"] = userName!, ["Item"] = itemName! });
                }

                return string.IsNullOrWhiteSpace(itemName) ? Localizer.T("PlaybackStopped") : Localizer.Format("PlaybackStoppedItem", new Dictionary<string, string> { ["Item"] = itemName! });

            case "ItemAdded":
                return string.IsNullOrWhiteSpace(itemName) ? Localizer.T("ItemAddedGeneric") : Localizer.Format("ItemAddedNamed", new Dictionary<string, string> { ["Item"] = itemName! });

            default:
                return eventName;
        }
    }

    private async Task<List<string>> PostJsonAsync(Dictionary<string, object?> body)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            using var msg = new HttpRequestMessage(HttpMethod.Post, RelayUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var res = await Http.SendAsync(msg).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var resp = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("Push relay error {StatusCode}: {Response}", (int)res.StatusCode, resp);
                return ParseInvalidTokens(resp);
            }
            else
            {
                var resp = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogInformation("Push delivered (status {StatusCode})", (int)res.StatusCode);
                return ParseInvalidTokens(resp);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push relay exception: {Message}", ex.Message);
            return new List<string>();
        }
    }

    private static List<string> ParseInvalidTokens(string json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("invalidTokens", out var invalidEl) && invalidEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var t in invalidEl.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String)
                        {
                            list.Add(t.GetString() ?? string.Empty);
                        }
                    }

                    return list.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return new List<string>();
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 8)
        {
            return "***";
        }

        var start = token.AsSpan(0, 4);
        var end = token.AsSpan(token.Length - 4, 4);
        var mask = new string('*', token.Length - 8).AsSpan();
        return string.Concat(start, mask, end);
    }
}
