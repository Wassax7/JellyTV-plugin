namespace Jellyfin.Plugin.JellyTV.Api.Models;

/// <summary>
/// Request model for sending a custom notification.
/// </summary>
public sealed class NotificationRequest
{
    /// <summary>
    /// Gets or sets the notification title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the notification body.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the target username or user id (N format). If empty, sends to all registered users.
    /// </summary>
    public string? Username { get; set; }
}
