using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Registration payload model.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin user id (Guid as string).
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the device token to register.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
