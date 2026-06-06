using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// App-facing server settings for JellyTV clients.
/// </summary>
public sealed class AppSettingsResponse
{
    /// <summary>
    /// Gets or sets the configured Seerr base URL, or an empty string when unset.
    /// </summary>
    [JsonPropertyName("seerrBaseUrl")]
    public string SeerrBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the active banner. Null when disabled or expired.
    /// </summary>
    [JsonPropertyName("banner")]
    public BannerSettingsDto? Banner { get; set; }

    /// <summary>
    /// Gets or sets the user's notification settings.
    /// </summary>
    [JsonPropertyName("notifications")]
    public AppNotificationSettingsDto Notifications { get; set; } = new AppNotificationSettingsDto();
}
