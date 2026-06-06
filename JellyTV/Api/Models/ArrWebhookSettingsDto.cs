using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Administrator settings for native *arr webhook notifications.
/// </summary>
public sealed class ArrWebhookSettingsDto
{
    /// <summary>
    /// Gets or sets the token required by native *arr webhook requests.
    /// </summary>
    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    /// <summary>
    /// Gets or sets the relative webhook path.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "/JellyTV/notifications";

    /// <summary>
    /// Gets or sets the relative webhook path including the token query string.
    /// </summary>
    [JsonPropertyName("relativeUrl")]
    public string RelativeUrl { get; set; } = string.Empty;
}
