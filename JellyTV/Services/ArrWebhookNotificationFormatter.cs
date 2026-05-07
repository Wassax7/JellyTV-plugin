using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Formats native Servarr webhook payloads into concise JellyTV push notifications.
/// </summary>
public sealed class ArrWebhookNotificationFormatter
{
    /// <summary>
    /// Converts a native *arr webhook payload into push notification text.
    /// </summary>
    /// <param name="payload">The JSON payload posted by an *arr webhook connection.</param>
    /// <returns>The formatted notification, or null when the payload is not a supported object.</returns>
    public ArrWebhookNotification? Format(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var eventType = GetString(payload, "eventType");
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return null;
        }

        var source = ResolveSource(payload);
        var eventLabel = GetEventLabel(eventType);
        var subject = ResolveSubject(payload);
        var details = ResolveDetails(payload, eventType);
        var body = JoinNonEmpty(subject, details);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = string.Equals(eventType, "Test", StringComparison.OrdinalIgnoreCase)
                ? "Webhook test received."
                : eventLabel;
        }

        return new ArrWebhookNotification
        {
            Title = $"{source}: {eventLabel}",
            Body = body,
            EventType = eventType
        };
    }

    private static string ResolveSource(JsonElement root)
    {
        var instanceName = GetString(root, "instanceName");
        if (!string.IsNullOrWhiteSpace(instanceName))
        {
            return instanceName;
        }

        if (TryGetObject(root, "movie", out _))
        {
            return "Radarr";
        }

        if (TryGetObject(root, "series", out _))
        {
            return "Sonarr";
        }

        if (TryGetObject(root, "artist", out _))
        {
            return "Lidarr";
        }

        if (TryGetObject(root, "author", out _) || TryGetObject(root, "book", out _))
        {
            return "Readarr";
        }

        return "Arr";
    }

    private static string ResolveSubject(JsonElement root)
    {
        var movieTitle = GetNestedString(root, "movie", "title");
        if (!string.IsNullOrWhiteSpace(movieTitle))
        {
            var year = GetNestedNumber(root, "movie", "year");
            return year.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{movieTitle} ({year.Value})")
                : movieTitle;
        }

        var seriesTitle = GetNestedString(root, "series", "title");
        if (!string.IsNullOrWhiteSpace(seriesTitle))
        {
            var episodeLabel = ResolveEpisodeLabel(root);
            return string.IsNullOrWhiteSpace(episodeLabel) ? seriesTitle : $"{seriesTitle} {episodeLabel}";
        }

        var artistName = GetNestedString(root, "artist", "artistName");
        var albumTitle = GetNestedString(root, "album", "title");
        if (!string.IsNullOrWhiteSpace(artistName) && !string.IsNullOrWhiteSpace(albumTitle))
        {
            return $"{artistName} - {albumTitle}";
        }

        if (!string.IsNullOrWhiteSpace(albumTitle))
        {
            return albumTitle;
        }

        if (!string.IsNullOrWhiteSpace(artistName))
        {
            return artistName;
        }

        var bookTitle = GetNestedString(root, "book", "title");
        var authorName = GetNestedString(root, "author", "authorName");
        if (!string.IsNullOrWhiteSpace(bookTitle) && !string.IsNullOrWhiteSpace(authorName))
        {
            return $"{authorName} - {bookTitle}";
        }

        if (!string.IsNullOrWhiteSpace(bookTitle))
        {
            return bookTitle;
        }

        if (!string.IsNullOrWhiteSpace(authorName))
        {
            return authorName;
        }

        var releaseTitle = GetNestedString(root, "release", "releaseTitle");
        if (!string.IsNullOrWhiteSpace(releaseTitle))
        {
            return releaseTitle;
        }

        return string.Empty;
    }

    private static string ResolveEpisodeLabel(JsonElement root)
    {
        if (!TryGetArray(root, "episodes", out var episodes))
        {
            return string.Empty;
        }

        var labels = new List<string>();
        foreach (var episode in episodes.EnumerateArray())
        {
            if (episode.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var season = GetNumber(episode, "seasonNumber");
            var episodeNumber = GetNumber(episode, "episodeNumber");
            var title = GetString(episode, "title");
            var code = season.HasValue && episodeNumber.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"S{season.Value:00}E{episodeNumber.Value:00}")
                : string.Empty;

            labels.Add(JoinNonEmpty(code, title));
        }

        if (labels.Count == 0)
        {
            return string.Empty;
        }

        return labels.Count == 1
            ? labels[0]
            : string.Create(CultureInfo.InvariantCulture, $"{labels[0]} and {labels.Count - 1} more");
    }

    private static string ResolveDetails(JsonElement root, string eventType)
    {
        if (string.Equals(eventType, "Test", StringComparison.OrdinalIgnoreCase))
        {
            return "Webhook test received.";
        }

        if (string.Equals(eventType, "Health", StringComparison.OrdinalIgnoreCase))
        {
            return JoinNonEmpty(
                GetNestedString(root, "healthIssue", "source"),
                GetNestedString(root, "healthIssue", "message"));
        }

        if (string.Equals(eventType, "HealthRestored", StringComparison.OrdinalIgnoreCase))
        {
            var source = GetNestedString(root, "healthIssue", "source");
            return string.IsNullOrWhiteSpace(source) ? "Health restored." : $"Health restored: {source}";
        }

        if (string.Equals(eventType, "ApplicationUpdate", StringComparison.OrdinalIgnoreCase))
        {
            var previous = GetNestedString(root, "update", "previousVersion");
            var current = GetNestedString(root, "update", "newVersion");
            return !string.IsNullOrWhiteSpace(previous) && !string.IsNullOrWhiteSpace(current)
                ? $"{previous} to {current}"
                : GetNestedString(root, "update", "message");
        }

        return JoinNonEmpty(
            GetNestedString(root, "release", "quality"),
            GetNestedString(root, "release", "releaseTitle"),
            GetString(root, "downloadClient"));
    }

    private static string GetEventLabel(string eventType)
        => eventType switch
        {
            "Grab" => "Grabbed",
            "Download" => "Imported",
            "Rename" => "Renamed",
            "SeriesAdd" => "Series added",
            "SeriesDelete" => "Series deleted",
            "EpisodeFileDelete" => "Episode deleted",
            "MovieAdded" => "Movie added",
            "MovieDelete" => "Movie deleted",
            "MovieFileDelete" => "Movie file deleted",
            "AlbumDelete" => "Album deleted",
            "AlbumDownload" => "Album imported",
            "BookDelete" => "Book deleted",
            "BookDownload" => "Book imported",
            "Health" => "Health alert",
            "HealthRestored" => "Health restored",
            "ApplicationUpdate" => "Application update",
            "ManualInteractionRequired" => "Manual action required",
            "Test" => "Test",
            _ => SplitPascalCase(eventType)
        };

    private static string JoinNonEmpty(params string?[] values)
        => string.Join(" - ", values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value!.Trim()));

    private static string SplitPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Notification";
        }

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(value[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private static string GetNestedString(JsonElement root, string objectName, string propertyName)
        => TryGetObject(root, objectName, out var child) ? GetString(child, propertyName) : string.Empty;

    private static int? GetNestedNumber(JsonElement root, string objectName, string propertyName)
        => TryGetObject(root, objectName, out var child) ? GetNumber(child, propertyName) : null;

    private static string GetString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static int? GetNumber(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static bool TryGetObject(JsonElement root, string propertyName, out JsonElement value)
        => TryGetProperty(root, propertyName, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement value)
        => TryGetProperty(root, propertyName, out value) && value.ValueKind == JsonValueKind.Array;

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
