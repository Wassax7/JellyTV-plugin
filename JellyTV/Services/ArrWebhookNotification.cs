namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Formatted push notification content produced from a native *arr webhook payload.
/// </summary>
public sealed class ArrWebhookNotification
{
    /// <summary>
    /// Gets or sets the push notification title.
    /// </summary>
    public string Title { get; set; } = "JellyTV";

    /// <summary>
    /// Gets or sets the push notification body.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source webhook event type.
    /// </summary>
    public string EventType { get; set; } = string.Empty;
}
