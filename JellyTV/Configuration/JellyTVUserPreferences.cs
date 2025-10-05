namespace Jellyfin.Plugin.JellyTV.Configuration;

/// <summary>
/// Per-user delivery preferences for JellyTV events. A null value means default allow.
/// </summary>
public sealed class JellyTVUserPreferences
{
    /// <summary>
    /// Gets or sets whether to receive ItemAdded notifications (null = allow by default).
    /// </summary>
    public bool? ForwardItemAdded { get; set; }

    /// <summary>
    /// Gets or sets whether to receive Playback notifications (start and stop) (null = allow by default).
    /// </summary>
    public bool? ForwardPlayback { get; set; }
}
