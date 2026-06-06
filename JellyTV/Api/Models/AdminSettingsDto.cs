using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Administrator-controlled JellyTV plugin settings.
/// </summary>
public sealed class AdminSettingsDto
{
    /// <summary>
    /// Gets or sets the configured Seerr base URL.
    /// </summary>
    [JsonPropertyName("seerrBaseUrl")]
    public string? SeerrBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the server-wide app banner settings.
    /// </summary>
    [JsonPropertyName("banner")]
    public BannerSettingsDto? Banner { get; set; }

    /// <summary>
    /// Gets or sets native *arr webhook settings.
    /// </summary>
    [JsonPropertyName("arrWebhooks")]
    public ArrWebhookSettingsDto? ArrWebhooks { get; set; }

    /// <summary>
    /// Gets or sets the global notification forwarding settings.
    /// </summary>
    [JsonPropertyName("notifications")]
    public NotificationSettingsDto? Notifications { get; set; }

    /// <summary>
    /// Gets or sets registered users and their notification settings.
    /// </summary>
    [JsonPropertyName("registeredUsers")]
    public IReadOnlyList<AdminRegisteredUserDto> RegisteredUsers { get; set; } = new List<AdminRegisteredUserDto>();
}
