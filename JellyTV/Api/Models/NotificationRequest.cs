using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Request model for sending a custom notification.
/// </summary>
public sealed class NotificationRequest
{
    /// <summary>
    /// Gets or sets the notification title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the notification body.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the target username payload. Accepts a string, an array of strings, null, or omission.
    /// </summary>
    [JsonPropertyName("username")]
    public JsonElement Username { get; set; }
}
