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

    /// <summary>
    /// Gets or sets a value indicating whether the ItemAdded preference was explicitly set by the user.
    /// </summary>
    public bool ItemAddedExplicit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the playback preference was explicitly set by the user.
    /// </summary>
    public bool PlaybackExplicit { get; set; }
}
