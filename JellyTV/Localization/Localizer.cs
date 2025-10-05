using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.JellyTV.Localization;

/// <summary>
/// Simple localization helper that maps string keys to localized values using in-memory dictionaries.
/// </summary>
internal static class Localizer
{
    private static readonly Dictionary<string, string> En = Dictionaries.En;
    private static readonly Dictionary<string, string> Fr = Dictionaries.Fr;
    private static readonly Dictionary<string, string> De = Dictionaries.De;

    private static Dictionary<string, string> Current
    {
        get
        {
            var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant();
            return code == "fr" ? Fr : code == "de" ? De : En;
        }
    }

    /// <summary>
    /// Gets a localized string for the specified key. Falls back to English and then the key.
    /// </summary>
    /// <param name="key">The message key.</param>
    /// <returns>The localized string or the key if not found.</returns>
    public static string T(string key)
    {
        if (Current.TryGetValue(key, out var value))
        {
            return value;
        }

        if (En.TryGetValue(key, out var en))
        {
            return en;
        }

        return key;
    }

    /// <summary>
    /// Formats a localized string with named placeholders.
    /// </summary>
    /// <param name="key">The message key.</param>
    /// <param name="values">Dictionary of placeholder values.</param>
    /// <returns>The formatted localized string.</returns>
    public static string Format(string key, IDictionary<string, string> values)
    {
        var template = T(key);
        if (values == null || values.Count == 0)
        {
            return template;
        }

        foreach (var kvp in values)
        {
            template = template.Replace("{" + kvp.Key + "}", kvp.Value ?? string.Empty, StringComparison.Ordinal);
        }

        return template;
    }

    private static class Dictionaries
    {
        public static readonly Dictionary<string, string> En = new()
        {
            // Generic
            ["Notification"] = "Notification",
            // Registration
            ["RegistrationBody"] = "You will now receive push notifications from JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Playback started",
            ["PlaybackStartedItem"] = "Playback started: {Item}",
            ["PlaybackStartedByUser"] = "{User} has started watching {Item}",
            ["PlaybackStopped"] = "Playback stopped",
            ["PlaybackStoppedItem"] = "Playback stopped: {Item}",
            ["PlaybackStoppedByUser"] = "{User} has stopped {Item}",
            // Item added
            ["ItemAddedGeneric"] = "New item available",
            ["ItemAddedNamed"] = "{Item} is now available",
            // Episode batch
            ["EpisodesNewNoSeries"] = "New episodes available ({Count} new)",
            ["EpisodeNewForSeries"] = "New episode available for {Series}",
            ["EpisodesNewForSeries"] = "New episodes available for {Series} ({Count} new)",
        };

        public static readonly Dictionary<string, string> Fr = new()
        {
            // Generic
            ["Notification"] = "Notification",
            // Registration
            ["RegistrationBody"] = "Vous recevrez désormais des notifications de JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Lecture démarrée",
            ["PlaybackStartedItem"] = "Lecture démarrée : {Item}",
            ["PlaybackStartedByUser"] = "{User} a commencé à regarder {Item}",
            ["PlaybackStopped"] = "Lecture arrêtée",
            ["PlaybackStoppedItem"] = "Lecture arrêtée : {Item}",
            ["PlaybackStoppedByUser"] = "{User} a arrêté {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nouvel élément disponible",
            ["ItemAddedNamed"] = "{Item} est maintenant disponible",
            // Episode batch
            ["EpisodesNewNoSeries"] = "Nouveaux épisodes disponibles ({Count} nouveau(x))",
            ["EpisodeNewForSeries"] = "Nouvel épisode disponible pour {Series}",
            ["EpisodesNewForSeries"] = "Nouveaux épisodes disponibles pour {Series} ({Count} nouveaux)",
        };

        public static readonly Dictionary<string, string> De = new()
        {
            // Generic
            ["Notification"] = "Benachrichtigung",
            // Registration
            ["RegistrationBody"] = "Sie erhalten nun Push‑Benachrichtigungen von JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Wiedergabe gestartet",
            ["PlaybackStartedItem"] = "Wiedergabe gestartet: {Item}",
            ["PlaybackStartedByUser"] = "{User} hat {Item} gestartet",
            ["PlaybackStopped"] = "Wiedergabe beendet",
            ["PlaybackStoppedItem"] = "Wiedergabe beendet: {Item}",
            ["PlaybackStoppedByUser"] = "{User} hat {Item} gestoppt",
            // Item added
            ["ItemAddedGeneric"] = "Neuer Inhalt verfügbar",
            ["ItemAddedNamed"] = "{Item} ist jetzt verfügbar",
            // Episode batch
            ["EpisodesNewNoSeries"] = "Neue Folgen verfügbar ({Count} neu)",
            ["EpisodeNewForSeries"] = "Neue Folge verfügbar für {Series}",
            ["EpisodesNewForSeries"] = "Neue Folgen verfügbar für {Series} ({Count} neu)",
        };
    }
}
