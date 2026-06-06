using System;
using System.Collections.ObjectModel;
using Jellyfin.Plugin.JellyTV.Utilities;
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
    private string? _seerrBaseUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // JellyTV defaults
        ForwardItemAdded = true;
        ForwardPlaybackStart = false;
        ForwardPlaybackStop = false;
        ArrWebhookSecret = CreateWebhookSecret();
        SeerrBaseUrl = string.Empty;
        BannerEnabled = false;
        BannerIconType = "info";
        BannerMessage = string.Empty;
        BannerExpiresAtUtc = null;
        OverrideServerLanguage = false;
        PreferredLanguage = "en";
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
    /// Gets or sets the URL token required for native *arr webhook requests.
    /// </summary>
    public string? ArrWebhookSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server-wide app banner is enabled.
    /// </summary>
    public bool BannerEnabled { get; set; }

    /// <summary>
    /// Gets or sets the server-wide app banner icon type.
    /// </summary>
    public string? BannerIconType { get; set; }

    /// <summary>
    /// Gets or sets the server-wide app banner message.
    /// </summary>
    public string? BannerMessage { get; set; }

    /// <summary>
    /// Gets or sets the optional UTC expiration timestamp for the server-wide app banner.
    /// Null means the banner does not expire.
    /// </summary>
    public string? BannerExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the Seerr base URL configured by the admin.
    /// Example: https://seerr.example.com.
    /// URL is validated on set; invalid URLs are rejected.
    /// </summary>
    public string? SeerrBaseUrl
    {
        get => _seerrBaseUrl;
        set
        {
            var (isValid, normalized, _) = UrlValidator.ValidateUrl(value);
            _seerrBaseUrl = isValid ? normalized : null;
        }
    }

    /// <summary>
    /// Gets or sets the previous serialized Seerr base URL field for upgrade compatibility.
    /// </summary>
    [System.Xml.Serialization.XmlElement("Jelly" + "seerrBaseUrl")]
    [System.Text.Json.Serialization.JsonPropertyName("Jelly" + "seerrBaseUrl")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacySeerrBaseUrl
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(_seerrBaseUrl) && !string.IsNullOrWhiteSpace(value))
            {
                SeerrBaseUrl = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether push notifications should use the plugin language instead of the Jellyfin server UI language.
    /// </summary>
    public bool OverrideServerLanguage { get; set; }

    /// <summary>
    /// Gets or sets the preferred language for push notifications.
    /// Supported values include "en", "ar", "zh", "hr", "cs", "da", "nl", "fr", "de", "he", "hi", "hu", "it", "nb", "pl", "pt-pt", "ru", "sl", "es", "es-419", "sv", "tr", and "uk".
    /// Defaults to "en" if not set or invalid.
    /// </summary>
    public string PreferredLanguage { get; set; }

    /// <summary>
    /// Gets the legacy list of registered users and their device tokens.
    /// Deprecated - kept for backward compatibility. Do not use.
    /// Not serialized: excluded from config file output.
    /// </summary>
    [System.Xml.Serialization.XmlIgnore]
    [System.Runtime.Serialization.IgnoreDataMember]
    [System.Text.Json.Serialization.JsonIgnore]
    public Collection<JellyTVUserTokens> RegisteredUsers { get; }

    /// <summary>
    /// Prevents writing the previous serialized Seerr base URL field after migration.
    /// </summary>
    /// <returns>Always false.</returns>
    public bool ShouldSerializeLegacySeerrBaseUrl() => false;

    private static string CreateWebhookSecret() => Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
}
