using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyTV.Configuration;

/// <summary>
/// Serializable model for a user's device tokens.
/// </summary>
public sealed class JellyTVUserTokens
{
    /// <summary>
    /// Gets or sets the Jellyfin user id (Guid as string).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the registered device tokens.
    /// </summary>
    public Collection<string> Tokens { get; } = new Collection<string>();
}
