using System;

namespace Jellyfin.Plugin.JellyTV.Utilities;

/// <summary>
/// Utility class for validating URLs used in plugin configuration.
/// </summary>
public static class UrlValidator
{
    private static readonly string[] DangerousSchemes = { "javascript", "data", "vbscript", "file", "about" };

    /// <summary>
    /// Validates a URL to ensure it's safe for use in configuration.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>Tuple of (isValid, normalizedUrl, errorMessage).</returns>
    public static (bool IsValid, string? NormalizedUrl, string? ErrorMessage) ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (true, null, null);
        }

        var trimmedUrl = url.Trim();

        if (!trimmedUrl.Contains("://", StringComparison.Ordinal))
        {
            trimmedUrl = "https://" + trimmedUrl;
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri))
        {
            return (false, null, "Invalid URL format");
        }

        foreach (var scheme in DangerousSchemes)
        {
            if (string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, $"URL scheme '{uri.Scheme}' is not permitted");
            }
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, $"URL scheme '{uri.Scheme}' is not allowed. Use http or https.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return (false, null, "URL must contain a valid host");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return (false, null, "URLs with embedded credentials are not permitted");
        }

        var normalizedUrl = uri.GetLeftPart(UriPartial.Authority);
        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            normalizedUrl += uri.AbsolutePath.TrimEnd('/');
        }

        return (true, normalizedUrl, null);
    }
}
