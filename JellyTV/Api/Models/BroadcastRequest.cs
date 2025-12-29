using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Request payload for sending a broadcast notification.
/// </summary>
public sealed class BroadcastRequest
{
    /// <summary>
    /// Gets or sets the message to broadcast.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
