using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyTV.Localization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Aggregates closely-timed episode ItemAdded events per series into a single push.
/// </summary>
/// <summary>
/// Batches episode additions by series and emits a single push after a short debounce window.
/// </summary>
public sealed class JellyTVEpisodeBatcher
{
    private readonly ILogger<JellyTVEpisodeBatcher> _logger;
    private readonly JellyTVPushService _pushService;
    private readonly TimeSpan _window = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _maxWindow = TimeSpan.FromSeconds(120);
    private readonly object _gate = new object();
    private readonly Dictionary<string, Batch> _batches = new Dictionary<string, Batch>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVEpisodeBatcher"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="pushService">The push delivery service.</param>
    public JellyTVEpisodeBatcher(ILogger<JellyTVEpisodeBatcher> logger, JellyTVPushService pushService)
    {
        _logger = logger;
        _pushService = pushService;
    }

    /// <summary>
    /// Enqueues an episode for batching by series name. Flushes after a short window.
    /// </summary>
    /// <param name="seriesKey">The series identifier key (preferably the Jellyfin series Id in N format, or series name as fallback).</param>
    /// <param name="seriesName">The series display name.</param>
    /// <param name="episodeId">The individual episode Id (N format).</param>
    /// <param name="episodeDisplayName">Display name for the specific episode triggering the batch.</param>
    /// <param name="seasonNumber">Season number associated with the episode.</param>
    /// <param name="episodeNumber">Episode number within the season.</param>
    public void Enqueue(string seriesKey, string seriesName, string episodeId, string episodeDisplayName, int? seasonNumber, int? episodeNumber)
    {
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            _logger.LogWarning(
                "Episode batch key missing; sending immediate fallback. episodeId={EpisodeId}; name={DisplayName}; season={Season}; episode={Episode}",
                episodeId,
                episodeDisplayName,
                seasonNumber,
                episodeNumber);
            _ = SendSingleEpisodeFallbackAsync(episodeId, seriesName, episodeDisplayName, seasonNumber, episodeNumber);
            return;
        }

        lock (_gate)
        {
            if (!_batches.TryGetValue(seriesKey, out var batch))
            {
                batch = new Batch(seriesName);
                _batches[seriesKey] = batch;
            }

            batch.Count++;
            batch.LastUpdated = DateTimeOffset.UtcNow;
            batch.SeriesName = string.IsNullOrWhiteSpace(seriesName) ? batch.SeriesName : seriesName;
            if (batch.Count == 1)
            {
                batch.SingleEpisodeId = string.IsNullOrWhiteSpace(episodeId) ? null : episodeId;
                batch.SingleEpisodeName = episodeDisplayName;
                batch.SingleSeasonNumber = seasonNumber;
                batch.SingleEpisodeNumber = episodeNumber;
                _logger.LogDebug(
                    "Episode batch started: seriesKey={SeriesKey}; name={SeriesName}; episodeId={EpisodeId}; season={Season}; episode={Episode}",
                    seriesKey,
                    batch.SeriesName,
                    batch.SingleEpisodeId,
                    batch.SingleSeasonNumber,
                    batch.SingleEpisodeNumber);
            }
            else
            {
                batch.SingleEpisodeId = null;
                batch.SingleEpisodeName = null;
                batch.SingleSeasonNumber = null;
                batch.SingleEpisodeNumber = null;
                _logger.LogDebug(
                    "Episode batch updated: seriesKey={SeriesKey}; newCount={Count}; activeBatches={ActiveBatches}",
                    seriesKey,
                    batch.Count,
                    _batches.Count);
            }

            // Check if batch has been open too long (from first episode) - force flush if exceeds max window
            var totalElapsed = DateTimeOffset.UtcNow - batch.FirstAdded;
            if (totalElapsed >= _maxWindow)
            {
                _logger.LogInformation(
                    "Force-flushing long-lived batch: seriesKey={SeriesKey}; totalElapsed={Elapsed}s; count={Count}",
                    seriesKey,
                    totalElapsed.TotalSeconds,
                    batch.Count);
                // Dispose existing timer and flush immediately
                batch.FlushTimer?.Dispose();
                batch.FlushTimer = null;
                FlushBatch(seriesKey);
                return;
            }

            // Dispose existing timer and create a new one (true debouncing)
            // Each new episode resets the countdown
            batch.FlushTimer?.Dispose();
            batch.FlushTimer = new Timer(
                callback: _ => FlushBatch(seriesKey),
                state: null,
                dueTime: _window,
                period: Timeout.InfiniteTimeSpan);

            _logger.LogDebug(
                "Batch timer reset: seriesKey={SeriesKey}; count={Count}; windowSeconds={Window}",
                seriesKey,
                batch.Count,
                _window.TotalSeconds);
        }
    }

    private void FlushBatch(string key)
    {
        int count;
        string name;
        string? singleEpisodeId;
        string? singleEpisodeName;
        int? singleSeasonNumber;
        int? singleEpisodeNumber;
        int activeBatchesRemaining;

        lock (_gate)
        {
            if (!_batches.TryGetValue(key, out var current))
            {
                _logger.LogDebug("Batch already flushed or removed: key={Key}", key);
                return; // already flushed/replaced
            }

            count = current.Count;
            name = current.SeriesName;
            singleEpisodeId = current.SingleEpisodeId;
            singleEpisodeName = current.SingleEpisodeName;
            singleSeasonNumber = current.SingleSeasonNumber;
            singleEpisodeNumber = current.SingleEpisodeNumber;

            // Dispose timer and remove batch from dictionary
            current.Dispose();
            _batches.Remove(key);
            activeBatchesRemaining = _batches.Count;
        }

        if (count <= 0)
        {
            return;
        }

        // Execute notification send asynchronously but don't block timer callback
        _ = Task.Run(async () =>
        {
            try
            {
                // Build message and recipients at send time to respect latest preferences
                var users = JellyTVUserStore.FilterUsersForEvent(
                    JellyTVUserStore.Load().Select(u => u.UserId),
                    "ItemAdded");

                _logger.LogInformation(
                    "Flushing episode batch: seriesKey={SeriesKey}; seriesName={SeriesName}; count={Count}; singleEpisodeId={EpisodeId}; season={Season}; episode={Episode}; users={UserCount}; activeBatchesRemaining={Remaining}",
                    key,
                    name,
                    count,
                    singleEpisodeId,
                    singleSeasonNumber,
                    singleEpisodeNumber,
                    users.Count,
                    activeBatchesRemaining);

                if (count == 1)
                {
                    // Build localized message inline - use only clean metadata to avoid file names
                    string friendlyBody;

                    if (singleSeasonNumber.HasValue && singleEpisodeNumber.HasValue && !string.IsNullOrWhiteSpace(name))
                    {
                        // Best case: series name + season/episode numbers available
                        friendlyBody = Localizer.Format(
                            "EpisodeNewForSeriesDetailed",
                            new Dictionary<string, string>
                            {
                                ["Series"] = name,
                                ["Season"] = singleSeasonNumber.Value.ToString(CultureInfo.InvariantCulture),
                                ["Episode"] = singleEpisodeNumber.Value.ToString(CultureInfo.InvariantCulture)
                            });
                    }
                    else if (!string.IsNullOrWhiteSpace(name))
                    {
                        // Fallback: only series name available, use episode batch format with count=1
                        friendlyBody = Localizer.Format("EpisodesNewForSeries", new Dictionary<string, string>
                        {
                            ["Series"] = name,
                            ["Count"] = "1"
                        });
                    }
                    else
                    {
                        // Last resort: no metadata available
                        friendlyBody = Localizer.T("ItemAddedGeneric");
                    }

                    var targetEpisodeId = string.IsNullOrWhiteSpace(singleEpisodeId) ? null : singleEpisodeId;

                    _logger.LogInformation(
                        "Episode push: series={Series}; episodeId={EpisodeId}; users={Users}; season={Season}; episode={Episode}",
                        name,
                        targetEpisodeId ?? string.Empty,
                        users.Count,
                        singleSeasonNumber,
                        singleEpisodeNumber);
                    await _pushService.SendEventAsync(
                        "ItemAdded",
                        targetEpisodeId,
                        users,
                        name,
                        bodyOverride: friendlyBody).ConfigureAwait(false);
                    return;
                }

                // Build batch notification message (count is always > 1 in this code path)
                string itemName = string.IsNullOrWhiteSpace(name)
                    ? Localizer.Format("EpisodesNewNoSeries", new Dictionary<string, string> { ["Count"] = count.ToString(CultureInfo.InvariantCulture) })
                    : Localizer.Format("EpisodesNewForSeries", new Dictionary<string, string> { ["Series"] = name, ["Count"] = count.ToString(CultureInfo.InvariantCulture) });

                _logger.LogInformation("Batch push: series={Series}; count={Count}; users={Users}", name, count, users.Count);
                await _pushService.SendEventAsync("ItemAdded", itemId: null, userIds: users, itemName: itemName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while flushing episode batch: {Message}", ex.Message);
            }
        });
    }

    private async Task SendSingleEpisodeFallbackAsync(string episodeId, string seriesName, string episodeDisplayName, int? seasonNumber, int? episodeNumber)
    {
        try
        {
            var users = JellyTVUserStore.FilterUsersForEvent(
                JellyTVUserStore.Load().Select(u => u.UserId),
                "ItemAdded");
            if (users.Count == 0)
            {
                return;
            }

            // Build localized message inline - use only clean metadata to avoid file names
            string friendlyBody;

            if (seasonNumber.HasValue && episodeNumber.HasValue && !string.IsNullOrWhiteSpace(seriesName))
            {
                // Best case: series name + season/episode numbers available
                friendlyBody = Localizer.Format(
                    "EpisodeNewForSeriesDetailed",
                    new Dictionary<string, string>
                    {
                        ["Series"] = seriesName,
                        ["Season"] = seasonNumber.Value.ToString(CultureInfo.InvariantCulture),
                        ["Episode"] = episodeNumber.Value.ToString(CultureInfo.InvariantCulture)
                    });
            }
            else if (!string.IsNullOrWhiteSpace(seriesName))
            {
                // Fallback: only series name available
                friendlyBody = Localizer.Format("ItemAddedNamed", new Dictionary<string, string> { ["Item"] = seriesName });
            }
            else
            {
                // Last resort: no metadata available
                friendlyBody = Localizer.T("ItemAddedGeneric");
            }

            _logger.LogInformation(
                "Episode fallback push: episodeId={EpisodeId}; users={Users}; message={Body}",
                episodeId,
                users.Count,
                friendlyBody);
            await _pushService.SendEventAsync(
                "ItemAdded",
                string.IsNullOrWhiteSpace(episodeId) ? null : episodeId,
                users,
                seriesName,
                bodyOverride: friendlyBody).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending fallback episode push: {Message}", ex.Message);
        }
    }

    private sealed class Batch : IDisposable
    {
        public Batch(string seriesName)
        {
            SeriesName = seriesName;
            var now = DateTimeOffset.UtcNow;
            FirstAdded = now;
            LastUpdated = now;
        }

        public string SeriesName { get; set; }

        public int Count { get; set; }

        public DateTimeOffset FirstAdded { get; }

        public DateTimeOffset LastUpdated { get; set; }

        public string? SingleEpisodeId { get; set; }

        public string? SingleEpisodeName { get; set; }

        public int? SingleSeasonNumber { get; set; }

        public int? SingleEpisodeNumber { get; set; }

        public Timer? FlushTimer { get; set; }

        public void Dispose()
        {
            FlushTimer?.Dispose();
            FlushTimer = null;
        }
    }
}
