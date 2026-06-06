using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Nullable per-user notification preferences. Null means use the administrator default.
/// </summary>
public sealed class UserPreferenceSettingsDto
{
    /// <summary>
    /// Gets or sets the nullable ItemAdded preference.
    /// </summary>
    [JsonPropertyName("forwardItemAdded")]
    public bool? ForwardItemAdded { get; set; }

    /// <summary>
    /// Gets or sets the nullable PlaybackStart preference.
    /// </summary>
    [JsonPropertyName("forwardPlaybackStart")]
    public bool? ForwardPlaybackStart { get; set; }

    /// <summary>
    /// Gets or sets the nullable PlaybackStop preference.
    /// </summary>
    [JsonPropertyName("forwardPlaybackStop")]
    public bool? ForwardPlaybackStop { get; set; }
}
