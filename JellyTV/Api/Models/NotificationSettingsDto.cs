using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Global notification forwarding settings controlled by an administrator.
/// </summary>
public sealed class NotificationSettingsDto
{
    /// <summary>
    /// Gets or sets a value indicating whether ItemAdded notifications are forwarded.
    /// </summary>
    [JsonPropertyName("forwardItemAdded")]
    public bool ForwardItemAdded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether PlaybackStart notifications are forwarded.
    /// </summary>
    [JsonPropertyName("forwardPlaybackStart")]
    public bool ForwardPlaybackStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether PlaybackStop notifications are forwarded.
    /// </summary>
    [JsonPropertyName("forwardPlaybackStop")]
    public bool ForwardPlaybackStop { get; set; }
}
