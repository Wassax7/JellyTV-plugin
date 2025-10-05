using System;
using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTV.Configuration;

/// <summary>
/// The configuration options.
/// </summary>
public enum SomeOptions
{
    /// <summary>
    /// Option one.
    /// </summary>
    OneOption,

    /// <summary>
    /// Second option.
    /// </summary>
    AnotherOption
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // JellyTV defaults
        ForwardItemAdded = false;
        ForwardPlaybackStart = true;
        ForwardPlaybackStop = true;
        JellyseerrBaseUrl = string.Empty;
        // Deprecated: RegisteredUsers moved to persistent store file.
        RegisteredUsers = new Collection<JellyTVUserTokens>();
    }

    /// <summary>
    /// Gets or sets a value indicating whether to forward ItemAdded events.
    /// </summary>
    public bool ForwardItemAdded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to forward PlaybackStart events.
    /// </summary>
    public bool ForwardPlaybackStart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to forward PlaybackStop events.
    /// </summary>
    public bool ForwardPlaybackStop { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr base URL configured by the admin.
    /// Example: https://jellyseerr.example.com.
    /// </summary>
    public string? JellyseerrBaseUrl { get; set; }

    /// <summary>
    /// Gets the legacy list of registered users and their device tokens.
    /// Deprecated - kept for backward compatibility. Do not use.
    /// Not serialized: excluded from config file output.
    /// </summary>
    [System.Xml.Serialization.XmlIgnore]
    [System.Runtime.Serialization.IgnoreDataMember]
    [System.Text.Json.Serialization.JsonIgnore]
    public Collection<JellyTVUserTokens> RegisteredUsers { get; }
}
