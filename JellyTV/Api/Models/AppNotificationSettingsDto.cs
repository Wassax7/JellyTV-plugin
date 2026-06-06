using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// App-facing notification settings for a user.
/// </summary>
public sealed class AppNotificationSettingsDto
{
    /// <summary>
    /// Gets or sets the global administrator defaults.
    /// </summary>
    [JsonPropertyName("adminDefaults")]
    public NotificationSettingsDto AdminDefaults { get; set; } = new NotificationSettingsDto();

    /// <summary>
    /// Gets or sets the user's nullable notification preferences.
    /// </summary>
    [JsonPropertyName("userPreferences")]
    public UserPreferenceSettingsDto UserPreferences { get; set; } = new UserPreferenceSettingsDto();

    /// <summary>
    /// Gets or sets the effective notification settings after applying global defaults and user preferences.
    /// </summary>
    [JsonPropertyName("effective")]
    public NotificationSettingsDto Effective { get; set; } = new NotificationSettingsDto();
}
