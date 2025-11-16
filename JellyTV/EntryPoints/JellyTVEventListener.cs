using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyTV.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.EntryPoints;

/// <summary>
/// Event listener entry point for JellyTV. Subscribes to Jellyfin events and forwards them if enabled.
/// </summary>
public sealed class JellyTVEventListener : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JellyTVEventListener> _logger;
    private readonly JellyTVPushService _pushService;
    private readonly JellyTVEpisodeBatcher _episodeBatcher;
    private readonly object _playbackGate = new object();
    private readonly Dictionary<string, DateTimeOffset> _recentPlaybackStarts = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _playbackStartCooldown = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVEventListener"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="pushService">The push service used to send notifications.</param>
    /// <param name="episodeBatcher">The batcher that aggregates closely-timed episode additions.</param>
    public JellyTVEventListener(ISessionManager sessionManager, ILibraryManager libraryManager, ILogger<JellyTVEventListener> logger, JellyTVPushService pushService, JellyTVEpisodeBatcher episodeBatcher)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _pushService = pushService;
        _episodeBatcher = episodeBatcher;
    }

    /// <inheritdoc />
    public Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyTV listener starting; subscribing to events.");
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyTV listener stopping; unsubscribing from events.");
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disposes the listener.
    /// </summary>
    public void Dispose()
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _libraryManager.ItemAdded -= OnItemAdded;
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        var plugin = Plugin.Instance;
        if (plugin?.Configuration?.ForwardPlaybackStart != true)
        {
            return;
        }

        var actorId = e.Session?.UserId ?? Guid.Empty;
        var actorUid = actorId == Guid.Empty ? string.Empty : actorId.ToString("N");
        var sessionId = e.Session?.Id ?? string.Empty;
        var itemId = e.Item?.Id.ToString("N");
        var dedupeKey = string.IsNullOrWhiteSpace(sessionId)
            ? $"{actorUid}|{itemId ?? string.Empty}"
            : $"{sessionId}|{itemId ?? string.Empty}";

        var now = DateTimeOffset.UtcNow;
        lock (_playbackGate)
        {
            if (_recentPlaybackStarts.TryGetValue(dedupeKey, out var last) && now - last < _playbackStartCooldown)
            {
                _logger.LogDebug("PlaybackStart suppressed due to cooldown. key={Key}; secondsSinceLast={DeltaSeconds:F1}", dedupeKey, (now - last).TotalSeconds);
                return;
            }

            _recentPlaybackStarts[dedupeKey] = now;

            if (_recentPlaybackStarts.Count > 100)
            {
                foreach (var stale in _recentPlaybackStarts.Where(kvp => now - kvp.Value >= _playbackStartCooldown * 2).Select(kvp => kvp.Key).ToList())
                {
                    _recentPlaybackStarts.Remove(stale);
                }
            }
        }

        // Send to all registered users, honoring per-user preferences
        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId);
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStart");
        if (userIds.Count == 0)
        {
            _logger.LogDebug("PlaybackStart event skipped: no opted-in recipients.");
            return;
        }

        var itemName = e.Item is Episode epStart ? (epStart.SeriesName ?? e.Item?.Name) : e.Item?.Name;
        var userName = e.Session?.UserName;
        _logger.LogDebug("PlaybackStart event: userIds={UserIds}; itemId={ItemId}; name={Name}; user={User}", string.Join(",", userIds), itemId, itemName, userName);
        await _pushService.SendEventAsync("PlaybackStart", itemId, userIds, itemName, userName).ConfigureAwait(false);
    }

    private async void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        var plugin = Plugin.Instance;
        if (plugin?.Configuration?.ForwardPlaybackStop != true)
        {
            return;
        }

        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId);
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStop");
        if (userIds.Count == 0)
        {
            _logger.LogDebug("PlaybackStop event skipped: no opted-in recipients.");
            return;
        }

        var itemId = e.Item?.Id.ToString("N");
        var itemName = e.Item is Episode epStop ? (epStop.SeriesName ?? e.Item?.Name) : e.Item?.Name;
        var userName = e.Session?.UserName;
        _logger.LogDebug("PlaybackStop event: userIds={UserIds}; itemId={ItemId}; name={Name}; user={User}", string.Join(",", userIds), itemId, itemName, userName);
        await _pushService.SendEventAsync("PlaybackStop", itemId, userIds, itemName, userName).ConfigureAwait(false);
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var plugin = Plugin.Instance;
        if (plugin?.Configuration?.ForwardItemAdded != true)
        {
            return;
        }

        // Filter: only forward for Movies, Series, and Episodes to avoid People/Artist/etc.
        var item = e.Item as BaseItem;
        if (item is null || !(item is Movie || item is Series || item is Episode))
        {
            return;
        }

        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(
            Services.JellyTVUserStore.Load().Select(u => u.UserId),
            "ItemAdded");
        if (userIds.Count == 0)
        {
            _logger.LogDebug("ItemAdded event skipped: no opted-in recipients. itemId={ItemId}", item.Id.ToString("N"));
            return;
        }

        if (item is Episode ep)
        {
            // Delay episode processing to allow Jellyfin's metadata refresh to complete
            // This gives time for Season creation and ParentIndexNumber/IndexNumber population
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

            // Re-fetch the episode to get updated metadata after Season creation
            var refreshedItem = _libraryManager.GetItemById(ep.Id);
            if (refreshedItem is not Episode refreshedEpisode)
            {
                _logger.LogWarning(
                    "Episode disappeared or changed type after metadata delay: episodeId={EpisodeId}",
                    ep.Id.ToString("N"));
                return;
            }

            // Batch per series within a short window while still tracking the specific episode that triggered the event.
            var identity = ResolveEpisodeBatchIdentity(refreshedEpisode);
            _logger.LogDebug(
                "Queueing episode ItemAdded: episodeId={EpisodeId}; seriesKey={SeriesKey}; seriesName={SeriesName}; displayName={DisplayName}; season={Season}; episode={Episode}; recipients={Recipients}",
                identity.EpisodeId,
                identity.SeriesKey,
                identity.SeriesName,
                identity.EpisodeDisplayName,
                identity.SeasonNumber,
                identity.EpisodeNumber,
                userIds.Count);
            _episodeBatcher.Enqueue(
                identity.SeriesKey,
                identity.SeriesName,
                identity.EpisodeId,
                identity.EpisodeDisplayName,
                identity.SeasonNumber,
                identity.EpisodeNumber);
            return;
        }

        var itemId = item.Id.ToString("N");
        var itemName = GetDisplayName(item);
        _logger.LogDebug("ItemAdded event: users={UserCount}; itemId={ItemId}; name={Name}", userIds.Count, itemId, itemName);
        await _pushService.SendEventAsync("ItemAdded", itemId, userIds, itemName).ConfigureAwait(false);
    }

    private static string GetDisplayName(BaseItem item)
    {
        if (item is Episode ep)
        {
            var series = ep.SeriesName ?? string.Empty;
            var s = ep.ParentIndexNumber;
            var e = ep.IndexNumber;
            var epName = ep.Name ?? string.Empty;
            if (s.HasValue && e.HasValue)
            {
                return string.IsNullOrWhiteSpace(series)
                    ? $"S{s.Value:00}E{e.Value:00} - {epName}"
                    : $"{series} S{s.Value:00}E{e.Value:00} - {epName}";
            }

            return string.IsNullOrWhiteSpace(series) ? (epName ?? item.Name ?? string.Empty) : $"{series} - {epName}";
        }

        return item.Name ?? string.Empty;
    }

    private static EpisodeBatchIdentity ResolveEpisodeBatchIdentity(Episode episode)
    {
        string key;

        // Primary: Use episode.SeriesId if populated
        if (episode.SeriesId != Guid.Empty)
        {
            key = episode.SeriesId.ToString("N");
        }
        else
        {
            // Secondary: Call FindSeriesId() to navigate parent hierarchy
            var foundSeriesId = episode.FindSeriesId();
            if (foundSeriesId != Guid.Empty)
            {
                key = foundSeriesId.ToString("N");
            }
            else
            {
                // Tertiary: Use SeriesName string property
                var rawSeriesName = episode.SeriesName;
                if (!string.IsNullOrWhiteSpace(rawSeriesName))
                {
                    // Disambiguate same-named series by including season ID if available
                    // This prevents episodes from different series with identical names from batching together
                    var seasonSuffix = episode.SeasonId != Guid.Empty
                        ? $"|{episode.SeasonId:N}"
                        : string.Empty;
                    key = rawSeriesName + seasonSuffix;
                }
                else
                {
                    // Last resort: Use episode ID (each episode gets separate batch)
                    key = episode.Id.ToString("N");
                }
            }
        }

        // Enhanced series name resolution with file name detection and fallback chain
        var seriesName = episode.SeriesName;

        // Check if SeriesName looks like a file name (e.g., "Beastars.E01" or has file extensions)
        if (!string.IsNullOrWhiteSpace(seriesName) && LooksLikeFileName(seriesName))
        {
            seriesName = null; // Force fallback chain
        }

        if (string.IsNullOrWhiteSpace(seriesName))
        {
            // Fallback chain: Series.Name → Season.SeriesName → FindSeriesName() → Season.Name
            var series = episode.Series;
            if (!string.IsNullOrWhiteSpace(series?.Name))
            {
                seriesName = series.Name;
            }
            else
            {
                var season = episode.Season;
                if (!string.IsNullOrWhiteSpace(season?.SeriesName))
                {
                    seriesName = season.SeriesName;
                }
                else
                {
                    var foundSeriesName = episode.FindSeriesName();
                    if (!string.IsNullOrWhiteSpace(foundSeriesName))
                    {
                        seriesName = foundSeriesName;
                    }
                    else if (!string.IsNullOrWhiteSpace(season?.Name))
                    {
                        seriesName = season.Name;
                    }
                }
            }
        }

        var displayName = GetDisplayName(episode);

        if (string.IsNullOrWhiteSpace(seriesName))
        {
            seriesName = displayName;
        }

        // Enhanced season/episode number resolution with Season object fallback
        var seasonNumber = episode.ParentIndexNumber;
        var episodeNumber = episode.IndexNumber;

        // Fallback: Try to get season number from Season object if ParentIndexNumber is null
        if (!seasonNumber.HasValue)
        {
            var season = episode.Season;
            if (season?.IndexNumber != null)
            {
                seasonNumber = season.IndexNumber;
            }
        }

        var episodeId = episode.Id != Guid.Empty ? episode.Id.ToString("N") : string.Empty;

        return new EpisodeBatchIdentity(
            key,
            seriesName ?? string.Empty,
            episodeId,
            displayName,
            seasonNumber,
            episodeNumber);
    }

    /// <summary>
    /// Checks if a string looks like a file name rather than a clean series/episode name.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True if the name appears to be a file name.</returns>
    private static bool LooksLikeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Check for common video file extensions
        if (name.Contains(".mkv", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".mp4", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".avi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".m4v", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".wmv", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check for dot-separated format without spaces (e.g., "Beastars.E01" or "Show.Name.S01E05")
        // Clean series names typically have spaces, file names use dots as separators
        if (name.Contains('.', StringComparison.Ordinal) && !name.Contains(' ', StringComparison.Ordinal))
        {
            // Additional check: Look for episode patterns like .E01 or .S01E05
            if (System.Text.RegularExpressions.Regex.IsMatch(
                name,
                @"\.[Ee]\d{1,4}|[Ss]\d{1,4}[Ee]\d{1,4}",
                System.Text.RegularExpressions.RegexOptions.None))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct EpisodeBatchIdentity(
        string SeriesKey,
        string SeriesName,
        string EpisodeId,
        string EpisodeDisplayName,
        int? SeasonNumber,
        int? EpisodeNumber);
}
