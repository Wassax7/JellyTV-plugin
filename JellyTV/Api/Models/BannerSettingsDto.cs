using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Server-wide message banner settings exposed to JellyTV clients.
/// </summary>
public sealed class BannerSettingsDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the banner is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the banner icon type.
    /// </summary>
    [JsonPropertyName("iconType")]
    public string? IconType { get; set; }

    /// <summary>
    /// Gets or sets the banner message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the optional UTC expiration timestamp. Null means no expiration.
    /// </summary>
    [JsonPropertyName("expiresAtUtc")]
    public string? ExpiresAtUtc { get; set; }
}
