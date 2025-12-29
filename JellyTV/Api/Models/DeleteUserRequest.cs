using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Request payload for deleting a user registration.
/// </summary>
public sealed class DeleteUserRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin user id to delete.
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }
}
