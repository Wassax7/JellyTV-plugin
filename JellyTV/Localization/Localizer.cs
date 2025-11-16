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
    private static readonly Dictionary<string, string> Es = Dictionaries.Es;
    private static readonly Dictionary<string, string> It = Dictionaries.It;
    private static readonly Dictionary<string, string> Pt = Dictionaries.Pt;
    private static readonly Dictionary<string, string> Hi = Dictionaries.Hi;

    private static Dictionary<string, string> Current
    {
        get
        {
            // Use configured language instead of thread culture
            // This ensures consistent localization regardless of server thread culture
            var code = Plugin.Instance?.Configuration?.PreferredLanguage?.ToLowerInvariant() ?? "en";
            return code switch
            {
                "fr" => Fr,
                "de" => De,
                "es" => Es,
                "it" => It,
                "pt" => Pt,
                "hi" => Hi,
                _ => En
            };
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
            ["EpisodesNewNoSeries"] = "{Count} new episodes available",
            ["EpisodesNewForSeries"] = "{Count} new episodes available for {Series}",
            ["EpisodeNewForSeriesDetailed"] = "New episode {Episode} from season {Season} available for {Series}",
            ["EpisodeNewDetailedNoSeries"] = "New episode {Episode} from season {Season} available",
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
            ["EpisodesNewNoSeries"] = "{Count} nouveaux épisodes disponibles",
            ["EpisodesNewForSeries"] = "{Count} nouveaux épisodes disponibles pour {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nouvel épisode {Episode} de la saison {Season} disponible pour {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nouvel épisode {Episode} de la saison {Season} disponible",
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
            ["EpisodesNewNoSeries"] = "{Count} neue Folgen verfügbar",
            ["EpisodesNewForSeries"] = "{Count} neue Folgen verfügbar für {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Neue Folge {Episode} aus Staffel {Season} verfügbar für {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Neue Folge {Episode} aus Staffel {Season} verfügbar",
        };

        public static readonly Dictionary<string, string> Es = new()
        {
            // Generic
            ["Notification"] = "Notificación",
            // Registration
            ["RegistrationBody"] = "Ahora recibirás notificaciones push de JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Reproducción iniciada",
            ["PlaybackStartedItem"] = "Reproducción iniciada: {Item}",
            ["PlaybackStartedByUser"] = "{User} ha empezado a ver {Item}",
            ["PlaybackStopped"] = "Reproducción detenida",
            ["PlaybackStoppedItem"] = "Reproducción detenida: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ha detenido {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nuevo contenido disponible",
            ["ItemAddedNamed"] = "{Item} ya está disponible",
            // Episode batch
            ["EpisodesNewNoSeries"] = "{Count} nuevos episodios disponibles",
            ["EpisodesNewForSeries"] = "{Count} nuevos episodios disponibles para {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nuevo episodio {Episode} de la temporada {Season} disponible para {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nuevo episodio {Episode} de la temporada {Season} disponible",
        };

        public static readonly Dictionary<string, string> It = new()
        {
            // Generic
            ["Notification"] = "Notifica",
            // Registration
            ["RegistrationBody"] = "Ora riceverai le notifiche push di JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Riproduzione avviata",
            ["PlaybackStartedItem"] = "Riproduzione avviata: {Item}",
            ["PlaybackStartedByUser"] = "{User} ha iniziato a guardare {Item}",
            ["PlaybackStopped"] = "Riproduzione terminata",
            ["PlaybackStoppedItem"] = "Riproduzione terminata: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ha interrotto {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nuovo contenuto disponibile",
            ["ItemAddedNamed"] = "{Item} è ora disponibile",
            // Episode batch
            ["EpisodesNewNoSeries"] = "{Count} nuovi episodi disponibili",
            ["EpisodesNewForSeries"] = "{Count} nuovi episodi disponibili per {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nuovo episodio {Episode} della stagione {Season} disponibile per {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nuovo episodio {Episode} della stagione {Season} disponibile",
        };

        public static readonly Dictionary<string, string> Pt = new()
        {
            // Generic
            ["Notification"] = "Notificação",
            // Registration
            ["RegistrationBody"] = "Agora você receberá notificações push do JellyTV.",
            // Playback
            ["PlaybackStarted"] = "Reprodução iniciada",
            ["PlaybackStartedItem"] = "Reprodução iniciada: {Item}",
            ["PlaybackStartedByUser"] = "{User} começou a assistir {Item}",
            ["PlaybackStopped"] = "Reprodução parada",
            ["PlaybackStoppedItem"] = "Reprodução parada: {Item}",
            ["PlaybackStoppedByUser"] = "{User} parou {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Novo conteúdo disponível",
            ["ItemAddedNamed"] = "{Item} está agora disponível",
            // Episode batch
            ["EpisodesNewNoSeries"] = "{Count} novos episódios disponíveis",
            ["EpisodesNewForSeries"] = "{Count} novos episódios disponíveis para {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Novo episódio {Episode} da temporada {Season} disponível para {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Novo episódio {Episode} da temporada {Season} disponível",
        };

        public static readonly Dictionary<string, string> Hi = new()
        {
            // Generic
            ["Notification"] = "सूचना",
            // Registration
            ["RegistrationBody"] = "अब आपको JellyTV से पुश सूचनाएं प्राप्त होंगी।",
            // Playback
            ["PlaybackStarted"] = "प्लेबैक शुरू हुआ",
            ["PlaybackStartedItem"] = "प्लेबैक शुरू हुआ: {Item}",
            ["PlaybackStartedByUser"] = "{User} ने {Item} देखना शुरू किया",
            ["PlaybackStopped"] = "प्लेबैक रुका",
            ["PlaybackStoppedItem"] = "प्लेबैक रुका: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ने {Item} रोक दिया",
            // Item added
            ["ItemAddedGeneric"] = "नई सामग्री उपलब्ध है",
            ["ItemAddedNamed"] = "{Item} अब उपलब्ध है",
            // Episode batch
            ["EpisodesNewNoSeries"] = "{Count} नए एपिसोड उपलब्ध हैं",
            ["EpisodesNewForSeries"] = "{Series} के लिए {Count} नए एपिसोड उपलब्ध हैं",
            ["EpisodeNewForSeriesDetailed"] = "{Series} के लिए सीज़न {Season} का नया एपिसोड {Episode} उपलब्ध है",
            ["EpisodeNewDetailedNoSeries"] = "सीज़न {Season} का नया एपिसोड {Episode} उपलब्ध है",
        };
    }
}
