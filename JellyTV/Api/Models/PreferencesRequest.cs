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
    /// Gets or sets whether to receive Playback notifications (start and stop) (null = default allow).
    /// </summary>
    public bool? ForwardPlayback { get; set; }
}
