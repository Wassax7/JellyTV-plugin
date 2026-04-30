using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Configuration;

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
    private static readonly Dictionary<string, string> Ar = Dictionaries.Ar;
    private static readonly Dictionary<string, string> Zh = Dictionaries.Zh;
    private static readonly Dictionary<string, string> Hr = Dictionaries.Hr;
    private static readonly Dictionary<string, string> Cs = Dictionaries.Cs;
    private static readonly Dictionary<string, string> Da = Dictionaries.Da;
    private static readonly Dictionary<string, string> He = Dictionaries.He;
    private static readonly Dictionary<string, string> Hu = Dictionaries.Hu;
    private static readonly Dictionary<string, string> Nb = Dictionaries.Nb;
    private static readonly Dictionary<string, string> Nl = Dictionaries.Nl;
    private static readonly Dictionary<string, string> Pl = Dictionaries.Pl;
    private static readonly Dictionary<string, string> Ru = Dictionaries.Ru;
    private static readonly Dictionary<string, string> Sl = Dictionaries.Sl;
    private static readonly Dictionary<string, string> Sv = Dictionaries.Sv;
    private static readonly Dictionary<string, string> Es419 = Dictionaries.Es419;
    private static readonly Dictionary<string, string> Tr = Dictionaries.Tr;
    private static readonly Dictionary<string, string> Uk = Dictionaries.Uk;

    private static IServerConfigurationManager? _serverConfigurationManager;

    private static Dictionary<string, string> Current
    {
        get
        {
            var code = GetConfiguredLanguageCode();
            return code switch
            {
                "fr" => Fr,
                "de" => De,
                "es" => Es,
                "it" => It,
                "pt" => Pt,
                "hi" => Hi,
                "ar" => Ar,
                "zh" => Zh,
                "zh-cn" => Zh,
                "hr" => Hr,
                "cs" => Cs,
                "da" => Da,
                "he" => He,
                "hu" => Hu,
                "nb" => Nb,
                "nl" => Nl,
                "pl" => Pl,
                "pt-pt" => Pt,
                "ru" => Ru,
                "sl" => Sl,
                "sv" => Sv,
                "es-419" => Es419,
                "tr" => Tr,
                "uk" => Uk,
                _ => En
            };
        }
    }

    /// <summary>
    /// Sets the Jellyfin server configuration manager used to resolve the server UI language.
    /// </summary>
    /// <param name="serverConfigurationManager">The server configuration manager.</param>
    public static void SetServerConfigurationManager(IServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
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

    private static string GetConfiguredLanguageCode()
    {
        if (Plugin.Instance?.Configuration?.OverrideServerLanguage == true)
        {
            return NormalizeLanguageCode(Plugin.Instance.Configuration.PreferredLanguage) ?? "en";
        }

        var serverCode = NormalizeLanguageCode(_serverConfigurationManager?.Configuration?.UICulture);
        if (!string.IsNullOrWhiteSpace(serverCode))
        {
            return serverCode;
        }

        return NormalizeLanguageCode(Plugin.Instance?.Configuration?.PreferredLanguage) ?? "en";
    }

    private static string? NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().Replace('_', '-').ToLowerInvariant();
        if (string.Equals(normalized, "zh-hans", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "zh-cn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "cmn-hans", StringComparison.OrdinalIgnoreCase))
        {
            return "zh";
        }

        if (string.Equals(normalized, "pt", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("pt-", StringComparison.OrdinalIgnoreCase))
        {
            return "pt-pt";
        }

        if (string.Equals(normalized, "es-419", StringComparison.OrdinalIgnoreCase))
        {
            return "es-419";
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            normalized = culture.Name.ToLowerInvariant();
        }
        catch (CultureNotFoundException)
        {
            // Use the raw normalized value below.
        }

        return normalized switch
        {
            "nb-no" => "nb",
            "nn-no" => "nb",
            _ when normalized.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) => "zh",
            _ when normalized.StartsWith("es-", StringComparison.OrdinalIgnoreCase) => "es",
            _ when normalized.Length > 2 => normalized[..2],
            _ => normalized
        };
    }

    private static class Dictionaries
    {
        public static readonly Dictionary<string, string> En = new()
        {
            // Generic
            ["Notification"] = "Notification",
            // Playback
            ["PlaybackStarted"] = "Playback started",
            ["PlaybackStartedItem"] = "Playback started: {Item}",
            ["PlaybackStartedByUser"] = "{User} has started watching {Item}",
            ["PlaybackStopped"] = "Playback stopped",
            ["PlaybackStoppedItem"] = "Playback stopped: {Item}",
            ["PlaybackStoppedByUser"] = "{User} has stopped watching {Item}",
            // Item added
            ["ItemAddedGeneric"] = "New item available",
            ["ItemAddedNamed"] = "{Item} is now available",
            // Episode batch
            ["EpisodeNewForSeries"] = "New episode available for {Series}",
            ["EpisodesNewNoSeries"] = "{Count} new episodes available",
            ["EpisodesNewForSeries"] = "{Count} new episodes available for {Series}",
            ["EpisodeNewForSeriesDetailed"] = "New episode {Episode} from season {Season} available for {Series}",
            ["EpisodeNewDetailedNoSeries"] = "New episode {Episode} from season {Season} available",
        };

        public static readonly Dictionary<string, string> Fr = new()
        {
            // Generic
            ["Notification"] = "Notification",
            // Playback
            ["PlaybackStarted"] = "Lecture démarrée",
            ["PlaybackStartedItem"] = "Lecture démarrée : {Item}",
            ["PlaybackStartedByUser"] = "{User} a commencé à regarder {Item}",
            ["PlaybackStopped"] = "Lecture arrêtée",
            ["PlaybackStoppedItem"] = "Lecture arrêtée : {Item}",
            ["PlaybackStoppedByUser"] = "{User} a arrêté de regarder {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nouvel élément disponible",
            ["ItemAddedNamed"] = "{Item} est maintenant disponible",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nouvel épisode disponible pour {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nouveaux épisodes disponibles",
            ["EpisodesNewForSeries"] = "{Count} nouveaux épisodes disponibles pour {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nouvel épisode {Episode} de la saison {Season} disponible pour {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nouvel épisode {Episode} de la saison {Season} disponible",
        };

        public static readonly Dictionary<string, string> De = new()
        {
            // Generic
            ["Notification"] = "Benachrichtigung",
            // Playback
            ["PlaybackStarted"] = "Wiedergabe gestartet",
            ["PlaybackStartedItem"] = "Wiedergabe gestartet: {Item}",
            ["PlaybackStartedByUser"] = "{User} hat {Item} gestartet",
            ["PlaybackStopped"] = "Wiedergabe beendet",
            ["PlaybackStoppedItem"] = "Wiedergabe beendet: {Item}",
            ["PlaybackStoppedByUser"] = "{User} hat aufgehört, {Item} zu schauen",
            // Item added
            ["ItemAddedGeneric"] = "Neuer Inhalt verfügbar",
            ["ItemAddedNamed"] = "{Item} ist jetzt verfügbar",
            // Episode batch
            ["EpisodeNewForSeries"] = "Neue Folge für {Series} verfügbar",
            ["EpisodesNewNoSeries"] = "{Count} neue Folgen verfügbar",
            ["EpisodesNewForSeries"] = "{Count} neue Folgen verfügbar für {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Neue Folge {Episode} aus Staffel {Season} verfügbar für {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Neue Folge {Episode} aus Staffel {Season} verfügbar",
        };

        public static readonly Dictionary<string, string> Es = new()
        {
            // Generic
            ["Notification"] = "Notificación",
            // Playback
            ["PlaybackStarted"] = "Reproducción iniciada",
            ["PlaybackStartedItem"] = "Reproducción iniciada: {Item}",
            ["PlaybackStartedByUser"] = "{User} ha empezado a ver {Item}",
            ["PlaybackStopped"] = "Reproducción detenida",
            ["PlaybackStoppedItem"] = "Reproducción detenida: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ha dejado de ver {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nuevo contenido disponible",
            ["ItemAddedNamed"] = "{Item} ya está disponible",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nuevo episodio disponible para {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nuevos episodios disponibles",
            ["EpisodesNewForSeries"] = "{Count} nuevos episodios disponibles para {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nuevo episodio {Episode} de la temporada {Season} disponible para {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nuevo episodio {Episode} de la temporada {Season} disponible",
        };

        public static readonly Dictionary<string, string> It = new()
        {
            // Generic
            ["Notification"] = "Notifica",
            // Playback
            ["PlaybackStarted"] = "Riproduzione avviata",
            ["PlaybackStartedItem"] = "Riproduzione avviata: {Item}",
            ["PlaybackStartedByUser"] = "{User} ha iniziato a guardare {Item}",
            ["PlaybackStopped"] = "Riproduzione terminata",
            ["PlaybackStoppedItem"] = "Riproduzione terminata: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ha smesso di guardare {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nuovo contenuto disponibile",
            ["ItemAddedNamed"] = "{Item} è ora disponibile",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nuovo episodio disponibile per {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nuovi episodi disponibili",
            ["EpisodesNewForSeries"] = "{Count} nuovi episodi disponibili per {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nuovo episodio {Episode} della stagione {Season} disponibile per {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nuovo episodio {Episode} della stagione {Season} disponibile",
        };

        public static readonly Dictionary<string, string> Pt = new()
        {
            // Generic
            ["Notification"] = "Notificação",
            // Playback
            ["PlaybackStarted"] = "Reprodução iniciada",
            ["PlaybackStartedItem"] = "Reprodução iniciada: {Item}",
            ["PlaybackStartedByUser"] = "{User} começou a ver {Item}",
            ["PlaybackStopped"] = "Reprodução terminada",
            ["PlaybackStoppedItem"] = "Reprodução terminada: {Item}",
            ["PlaybackStoppedByUser"] = "{User} parou de ver {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Novo conteúdo disponível",
            ["ItemAddedNamed"] = "{Item} está agora disponível",
            // Episode batch
            ["EpisodeNewForSeries"] = "Novo episódio disponível para {Series}",
            ["EpisodesNewNoSeries"] = "{Count} novos episódios disponíveis",
            ["EpisodesNewForSeries"] = "{Count} novos episódios disponíveis para {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Novo episódio {Episode} da temporada {Season} disponível para {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Novo episódio {Episode} da temporada {Season} disponível",
        };

        public static readonly Dictionary<string, string> Hi = new()
        {
            // Generic
            ["Notification"] = "सूचना",
            // Playback
            ["PlaybackStarted"] = "प्लेबैक शुरू हुआ",
            ["PlaybackStartedItem"] = "प्लेबैक शुरू हुआ: {Item}",
            ["PlaybackStartedByUser"] = "{User} ने {Item} देखना शुरू किया",
            ["PlaybackStopped"] = "प्लेबैक रुका",
            ["PlaybackStoppedItem"] = "प्लेबैक रुका: {Item}",
            ["PlaybackStoppedByUser"] = "{User} ने {Item} देखना बंद कर दिया",
            // Item added
            ["ItemAddedGeneric"] = "नई सामग्री उपलब्ध है",
            ["ItemAddedNamed"] = "{Item} अब उपलब्ध है",
            // Episode batch
            ["EpisodeNewForSeries"] = "{Series} के लिए नया एपिसोड उपलब्ध है",
            ["EpisodesNewNoSeries"] = "{Count} नए एपिसोड उपलब्ध हैं",
            ["EpisodesNewForSeries"] = "{Series} के लिए {Count} नए एपिसोड उपलब्ध हैं",
            ["EpisodeNewForSeriesDetailed"] = "{Series} के लिए सीज़न {Season} का नया एपिसोड {Episode} उपलब्ध है",
            ["EpisodeNewDetailedNoSeries"] = "सीज़न {Season} का नया एपिसोड {Episode} उपलब्ध है",
        };

        public static readonly Dictionary<string, string> Ar = new()
        {
            // Generic
            ["Notification"] = "إشعار",
            // Playback
            ["PlaybackStarted"] = "بدأ التشغيل",
            ["PlaybackStartedItem"] = "بدأ التشغيل: {Item}",
            ["PlaybackStartedByUser"] = "{User} بدأ مشاهدة {Item}",
            ["PlaybackStopped"] = "توقف التشغيل",
            ["PlaybackStoppedItem"] = "توقف التشغيل: {Item}",
            ["PlaybackStoppedByUser"] = "{User} توقف عن مشاهدة {Item}",
            // Item added
            ["ItemAddedGeneric"] = "محتوى جديد متاح",
            ["ItemAddedNamed"] = "{Item} متاح الآن",
            // Episode batch
            ["EpisodeNewForSeries"] = "حلقة جديدة متاحة لـ {Series}",
            ["EpisodesNewNoSeries"] = "{Count} حلقات جديدة متاحة",
            ["EpisodesNewForSeries"] = "{Count} حلقات جديدة متاحة لـ {Series}",
            ["EpisodeNewForSeriesDetailed"] = "حلقة جديدة {Episode} من الموسم {Season} متاحة لـ {Series}",
            ["EpisodeNewDetailedNoSeries"] = "حلقة جديدة {Episode} من الموسم {Season} متاحة",
        };

        public static readonly Dictionary<string, string> Zh = new()
        {
            // Generic
            ["Notification"] = "通知",
            // Playback
            ["PlaybackStarted"] = "开始播放",
            ["PlaybackStartedItem"] = "开始播放：{Item}",
            ["PlaybackStartedByUser"] = "{User} 开始观看 {Item}",
            ["PlaybackStopped"] = "停止播放",
            ["PlaybackStoppedItem"] = "停止播放：{Item}",
            ["PlaybackStoppedByUser"] = "{User} 停止观看 {Item}",
            // Item added
            ["ItemAddedGeneric"] = "新内容可用",
            ["ItemAddedNamed"] = "{Item} 现已可用",
            // Episode batch
            ["EpisodeNewForSeries"] = "{Series} 有新剧集可用",
            ["EpisodesNewNoSeries"] = "{Count} 集新剧集可用",
            ["EpisodesNewForSeries"] = "{Series} 有 {Count} 集新剧集可用",
            ["EpisodeNewForSeriesDetailed"] = "{Series} 第 {Season} 季第 {Episode} 集现已可用",
            ["EpisodeNewDetailedNoSeries"] = "第 {Season} 季第 {Episode} 集现已可用",
        };

        public static readonly Dictionary<string, string> Hr = new()
        {
            // Generic
            ["Notification"] = "Obavijest",
            // Playback
            ["PlaybackStarted"] = "Reprodukcija je započela",
            ["PlaybackStartedItem"] = "Reprodukcija je započela: {Item}",
            ["PlaybackStartedByUser"] = "{User} je počeo/la gledati {Item}",
            ["PlaybackStopped"] = "Reprodukcija je zaustavljena",
            ["PlaybackStoppedItem"] = "Reprodukcija je zaustavljena: {Item}",
            ["PlaybackStoppedByUser"] = "{User} je prestao/la gledati {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Dostupan je novi sadržaj",
            ["ItemAddedNamed"] = "{Item} je sada dostupan",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nova epizoda dostupna je za {Series}",
            ["EpisodesNewNoSeries"] = "{Count} novih epizoda dostupno",
            ["EpisodesNewForSeries"] = "{Count} novih epizoda dostupno za {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nova epizoda {Episode} iz sezone {Season} dostupna je za {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nova epizoda {Episode} iz sezone {Season} dostupna je",
        };

        public static readonly Dictionary<string, string> Cs = new()
        {
            // Generic
            ["Notification"] = "Oznámení",
            // Playback
            ["PlaybackStarted"] = "Přehrávání spuštěno",
            ["PlaybackStartedItem"] = "Přehrávání spuštěno: {Item}",
            ["PlaybackStartedByUser"] = "{User} začal(a) sledovat {Item}",
            ["PlaybackStopped"] = "Přehrávání zastaveno",
            ["PlaybackStoppedItem"] = "Přehrávání zastaveno: {Item}",
            ["PlaybackStoppedByUser"] = "{User} přestal(a) sledovat {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Je dostupný nový obsah",
            ["ItemAddedNamed"] = "{Item} je nyní k dispozici",
            // Episode batch
            ["EpisodeNewForSeries"] = "Je dostupná nová epizoda seriálu {Series}",
            ["EpisodesNewNoSeries"] = "Je dostupných {Count} nových epizod",
            ["EpisodesNewForSeries"] = "Je dostupných {Count} nových epizod seriálu {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nová epizoda {Episode} z řady {Season} je dostupná pro {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nová epizoda {Episode} z řady {Season} je dostupná",
        };

        public static readonly Dictionary<string, string> Da = new()
        {
            // Generic
            ["Notification"] = "Notifikation",
            // Playback
            ["PlaybackStarted"] = "Afspilning startet",
            ["PlaybackStartedItem"] = "Afspilning startet: {Item}",
            ["PlaybackStartedByUser"] = "{User} er begyndt at se {Item}",
            ["PlaybackStopped"] = "Afspilning stoppet",
            ["PlaybackStoppedItem"] = "Afspilning stoppet: {Item}",
            ["PlaybackStoppedByUser"] = "{User} er stoppet med at se {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nyt indhold tilgængeligt",
            ["ItemAddedNamed"] = "{Item} er nu tilgængelig",
            // Episode batch
            ["EpisodeNewForSeries"] = "Ny episode tilgængelig for {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nye episoder tilgængelige",
            ["EpisodesNewForSeries"] = "{Count} nye episoder tilgængelige for {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Ny episode {Episode} fra sæson {Season} tilgængelig for {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Ny episode {Episode} fra sæson {Season} tilgængelig",
        };

        public static readonly Dictionary<string, string> He = new()
        {
            // Generic
            ["Notification"] = "התראה",
            // Playback
            ["PlaybackStarted"] = "ההפעלה התחילה",
            ["PlaybackStartedItem"] = "ההפעלה התחילה: {Item}",
            ["PlaybackStartedByUser"] = "{User} התחיל לצפות ב-{Item}",
            ["PlaybackStopped"] = "ההפעלה הופסקה",
            ["PlaybackStoppedItem"] = "ההפעלה הופסקה: {Item}",
            ["PlaybackStoppedByUser"] = "{User} הפסיק לצפות ב-{Item}",
            // Item added
            ["ItemAddedGeneric"] = "תוכן חדש זמין",
            ["ItemAddedNamed"] = "{Item} זמין כעת",
            // Episode batch
            ["EpisodeNewForSeries"] = "פרק חדש זמין עבור {Series}",
            ["EpisodesNewNoSeries"] = "{Count} פרקים חדשים זמינים",
            ["EpisodesNewForSeries"] = "{Count} פרקים חדשים זמינים עבור {Series}",
            ["EpisodeNewForSeriesDetailed"] = "פרק חדש {Episode} מעונה {Season} זמין עבור {Series}",
            ["EpisodeNewDetailedNoSeries"] = "פרק חדש {Episode} מעונה {Season} זמין",
        };

        public static readonly Dictionary<string, string> Hu = new()
        {
            // Generic
            ["Notification"] = "Értesítés",
            // Playback
            ["PlaybackStarted"] = "Lejátszás elindult",
            ["PlaybackStartedItem"] = "Lejátszás elindult: {Item}",
            ["PlaybackStartedByUser"] = "{User} elkezdte nézni: {Item}",
            ["PlaybackStopped"] = "Lejátszás leállítva",
            ["PlaybackStoppedItem"] = "Lejátszás leállítva: {Item}",
            ["PlaybackStoppedByUser"] = "{User} abbahagyta a(z) {Item} nézését",
            // Item added
            ["ItemAddedGeneric"] = "Új tartalom érhető el",
            ["ItemAddedNamed"] = "{Item} már elérhető",
            // Episode batch
            ["EpisodeNewForSeries"] = "Új epizód érhető el ehhez: {Series}",
            ["EpisodesNewNoSeries"] = "{Count} új epizód érhető el",
            ["EpisodesNewForSeries"] = "{Count} új epizód érhető el ehhez: {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Új {Episode}. epizód a(z) {Season}. évadból elérhető ehhez: {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Új {Episode}. epizód a(z) {Season}. évadból elérhető",
        };

        public static readonly Dictionary<string, string> Nb = new()
        {
            // Generic
            ["Notification"] = "Varsel",
            // Playback
            ["PlaybackStarted"] = "Avspilling startet",
            ["PlaybackStartedItem"] = "Avspilling startet: {Item}",
            ["PlaybackStartedByUser"] = "{User} har begynt å se {Item}",
            ["PlaybackStopped"] = "Avspilling stoppet",
            ["PlaybackStoppedItem"] = "Avspilling stoppet: {Item}",
            ["PlaybackStoppedByUser"] = "{User} har sluttet å se {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nytt innhold tilgjengelig",
            ["ItemAddedNamed"] = "{Item} er nå tilgjengelig",
            // Episode batch
            ["EpisodeNewForSeries"] = "Ny episode tilgjengelig for {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nye episoder tilgjengelig",
            ["EpisodesNewForSeries"] = "{Count} nye episoder tilgjengelig for {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Ny episode {Episode} fra sesong {Season} tilgjengelig for {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Ny episode {Episode} fra sesong {Season} tilgjengelig",
        };

        public static readonly Dictionary<string, string> Nl = new()
        {
            // Generic
            ["Notification"] = "Melding",
            // Playback
            ["PlaybackStarted"] = "Afspelen gestart",
            ["PlaybackStartedItem"] = "Afspelen gestart: {Item}",
            ["PlaybackStartedByUser"] = "{User} is begonnen met kijken naar {Item}",
            ["PlaybackStopped"] = "Afspelen gestopt",
            ["PlaybackStoppedItem"] = "Afspelen gestopt: {Item}",
            ["PlaybackStoppedByUser"] = "{User} is gestopt met kijken naar {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nieuwe inhoud beschikbaar",
            ["ItemAddedNamed"] = "{Item} is nu beschikbaar",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nieuwe aflevering beschikbaar voor {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nieuwe afleveringen beschikbaar",
            ["EpisodesNewForSeries"] = "{Count} nieuwe afleveringen beschikbaar voor {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nieuwe aflevering {Episode} van seizoen {Season} beschikbaar voor {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nieuwe aflevering {Episode} van seizoen {Season} beschikbaar",
        };

        public static readonly Dictionary<string, string> Pl = new()
        {
            // Generic
            ["Notification"] = "Powiadomienie",
            // Playback
            ["PlaybackStarted"] = "Rozpoczęto odtwarzanie",
            ["PlaybackStartedItem"] = "Rozpoczęto odtwarzanie: {Item}",
            ["PlaybackStartedByUser"] = "{User} rozpoczął oglądanie {Item}",
            ["PlaybackStopped"] = "Zatrzymano odtwarzanie",
            ["PlaybackStoppedItem"] = "Zatrzymano odtwarzanie: {Item}",
            ["PlaybackStoppedByUser"] = "{User} przestał oglądać {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nowa zawartość dostępna",
            ["ItemAddedNamed"] = "{Item} jest teraz dostępny",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nowy odcinek dostępny dla {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nowych odcinków dostępnych",
            ["EpisodesNewForSeries"] = "{Count} nowych odcinków dostępnych dla {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nowy odcinek {Episode} z sezonu {Season} dostępny dla {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nowy odcinek {Episode} z sezonu {Season} dostępny",
        };

        public static readonly Dictionary<string, string> Ru = new()
        {
            // Generic
            ["Notification"] = "Уведомление",
            // Playback
            ["PlaybackStarted"] = "Воспроизведение началось",
            ["PlaybackStartedItem"] = "Воспроизведение началось: {Item}",
            ["PlaybackStartedByUser"] = "{User} начал(а) смотреть {Item}",
            ["PlaybackStopped"] = "Воспроизведение остановлено",
            ["PlaybackStoppedItem"] = "Воспроизведение остановлено: {Item}",
            ["PlaybackStoppedByUser"] = "{User} перестал(а) смотреть {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Доступен новый контент",
            ["ItemAddedNamed"] = "{Item} теперь доступен",
            // Episode batch
            ["EpisodeNewForSeries"] = "Новый эпизод доступен для {Series}",
            ["EpisodesNewNoSeries"] = "Доступно новых эпизодов: {Count}",
            ["EpisodesNewForSeries"] = "Для {Series} доступно новых эпизодов: {Count}",
            ["EpisodeNewForSeriesDetailed"] = "Новый эпизод {Episode} из сезона {Season} доступен для {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Новый эпизод {Episode} из сезона {Season} доступен",
        };

        public static readonly Dictionary<string, string> Sl = new()
        {
            // Generic
            ["Notification"] = "Obvestilo",
            // Playback
            ["PlaybackStarted"] = "Predvajanje se je začelo",
            ["PlaybackStartedItem"] = "Predvajanje se je začelo: {Item}",
            ["PlaybackStartedByUser"] = "{User} je začel(a) gledati {Item}",
            ["PlaybackStopped"] = "Predvajanje je ustavljeno",
            ["PlaybackStoppedItem"] = "Predvajanje je ustavljeno: {Item}",
            ["PlaybackStoppedByUser"] = "{User} je prenehal(a) gledati {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Na voljo je nova vsebina",
            ["ItemAddedNamed"] = "{Item} je zdaj na voljo",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nova epizoda je na voljo za {Series}",
            ["EpisodesNewNoSeries"] = "Na voljo je {Count} novih epizod",
            ["EpisodesNewForSeries"] = "Za {Series} je na voljo {Count} novih epizod",
            ["EpisodeNewForSeriesDetailed"] = "Nova epizoda {Episode} iz sezone {Season} je na voljo za {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nova epizoda {Episode} iz sezone {Season} je na voljo",
        };

        public static readonly Dictionary<string, string> Sv = new()
        {
            // Generic
            ["Notification"] = "Avisering",
            // Playback
            ["PlaybackStarted"] = "Uppspelning startad",
            ["PlaybackStartedItem"] = "Uppspelning startad: {Item}",
            ["PlaybackStartedByUser"] = "{User} har börjat titta på {Item}",
            ["PlaybackStopped"] = "Uppspelning stoppad",
            ["PlaybackStoppedItem"] = "Uppspelning stoppad: {Item}",
            ["PlaybackStoppedByUser"] = "{User} har slutat titta på {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nytt innehåll tillgängligt",
            ["ItemAddedNamed"] = "{Item} är nu tillgängligt",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nytt avsnitt tillgängligt för {Series}",
            ["EpisodesNewNoSeries"] = "{Count} nya avsnitt tillgängliga",
            ["EpisodesNewForSeries"] = "{Count} nya avsnitt tillgängliga för {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nytt avsnitt {Episode} från säsong {Season} tillgängligt för {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nytt avsnitt {Episode} från säsong {Season} tillgängligt",
        };

        public static readonly Dictionary<string, string> Es419 = new()
        {
            // Generic
            ["Notification"] = "Notificación",
            // Playback
            ["PlaybackStarted"] = "Reproducción iniciada",
            ["PlaybackStartedItem"] = "Reproducción iniciada: {Item}",
            ["PlaybackStartedByUser"] = "{User} empezó a ver {Item}",
            ["PlaybackStopped"] = "Reproducción detenida",
            ["PlaybackStoppedItem"] = "Reproducción detenida: {Item}",
            ["PlaybackStoppedByUser"] = "{User} dejó de ver {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Nuevo contenido disponible",
            ["ItemAddedNamed"] = "{Item} ya está disponible",
            // Episode batch
            ["EpisodeNewForSeries"] = "Nuevo episodio disponible para {Series}",
            ["EpisodesNewNoSeries"] = "{Count} episodios nuevos disponibles",
            ["EpisodesNewForSeries"] = "{Count} episodios nuevos disponibles para {Series}",
            ["EpisodeNewForSeriesDetailed"] = "Nuevo episodio {Episode} de la temporada {Season} disponible para {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Nuevo episodio {Episode} de la temporada {Season} disponible",
        };

        public static readonly Dictionary<string, string> Tr = new()
        {
            // Generic
            ["Notification"] = "Bildirim",
            // Playback
            ["PlaybackStarted"] = "Oynatma başladı",
            ["PlaybackStartedItem"] = "Oynatma başladı: {Item}",
            ["PlaybackStartedByUser"] = "{User}, {Item} izlemeye başladı",
            ["PlaybackStopped"] = "Oynatma durduruldu",
            ["PlaybackStoppedItem"] = "Oynatma durduruldu: {Item}",
            ["PlaybackStoppedByUser"] = "{User}, {Item} izlemeyi bıraktı",
            // Item added
            ["ItemAddedGeneric"] = "Yeni içerik mevcut",
            ["ItemAddedNamed"] = "{Item} artık mevcut",
            // Episode batch
            ["EpisodeNewForSeries"] = "{Series} için yeni bölüm mevcut",
            ["EpisodesNewNoSeries"] = "{Count} yeni bölüm mevcut",
            ["EpisodesNewForSeries"] = "{Series} için {Count} yeni bölüm mevcut",
            ["EpisodeNewForSeriesDetailed"] = "{Series} için sezon {Season} bölüm {Episode} mevcut",
            ["EpisodeNewDetailedNoSeries"] = "Sezon {Season} bölüm {Episode} mevcut",
        };

        public static readonly Dictionary<string, string> Uk = new()
        {
            // Generic
            ["Notification"] = "Сповіщення",
            // Playback
            ["PlaybackStarted"] = "Відтворення розпочато",
            ["PlaybackStartedItem"] = "Відтворення розпочато: {Item}",
            ["PlaybackStartedByUser"] = "{User} почав(ла) дивитися {Item}",
            ["PlaybackStopped"] = "Відтворення зупинено",
            ["PlaybackStoppedItem"] = "Відтворення зупинено: {Item}",
            ["PlaybackStoppedByUser"] = "{User} припинив(ла) дивитися {Item}",
            // Item added
            ["ItemAddedGeneric"] = "Доступний новий вміст",
            ["ItemAddedNamed"] = "{Item} тепер доступний",
            // Episode batch
            ["EpisodeNewForSeries"] = "Новий епізод доступний для {Series}",
            ["EpisodesNewNoSeries"] = "Доступно нових епізодів: {Count}",
            ["EpisodesNewForSeries"] = "Для {Series} доступно нових епізодів: {Count}",
            ["EpisodeNewForSeriesDetailed"] = "Новий епізод {Episode} із сезону {Season} доступний для {Series}",
            ["EpisodeNewDetailedNoSeries"] = "Новий епізод {Episode} із сезону {Season} доступний",
        };
    }
}
