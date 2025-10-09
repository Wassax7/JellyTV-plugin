namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Request model to set per-user JellyTV event preferences.
/// </summary>
public sealed class PreferencesRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin user id (Guid as string).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to receive ItemAdded notifications (null = default allow).
    /// </summary>
    public bool? ForwardItemAdded { get; set; }

    /// <summary>
    /// Gets or sets whether to receive PlaybackStart notifications (null = default allow).
    /// </summary>
    public bool? ForwardPlaybackStart { get; set; }

    /// <summary>
    /// Gets or sets whether to receive PlaybackStop notifications (null = default allow).
    /// </summary>
    public bool? ForwardPlaybackStop { get; set; }
}
