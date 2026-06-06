using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Registered user summary for administrator settings responses.
/// </summary>
public sealed class AdminRegisteredUserDto
{
    /// <summary>
    /// Gets or sets the normalized Jellyfin user identifier.
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current Jellyfin profile name, or null when the user no longer exists.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the referenced Jellyfin user no longer exists.
    /// </summary>
    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the user's nullable notification preferences.
    /// </summary>
    [JsonPropertyName("preferences")]
    public UserPreferenceSettingsDto Preferences { get; set; } = new UserPreferenceSettingsDto();

    /// <summary>
    /// Gets or sets the user's effective notification settings.
    /// </summary>
    [JsonPropertyName("effective")]
    public NotificationSettingsDto Effective { get; set; } = new NotificationSettingsDto();
}
