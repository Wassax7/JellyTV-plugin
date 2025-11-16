using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Unregistration payload model.
/// </summary>
public sealed class UnregisterRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin user id (Guid as string).
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the device token to unregister.
    /// </summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
