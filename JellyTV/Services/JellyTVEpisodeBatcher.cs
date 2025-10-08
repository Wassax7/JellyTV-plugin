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
    private readonly TimeSpan _window = TimeSpan.FromSeconds(10);
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

        int scheduledVersion;
        lock (_gate)
        {
            if (!_batches.TryGetValue(seriesKey, out var batch))
            {
                batch = new Batch(seriesName);
                _batches[seriesKey] = batch;
            }

            batch.Count++;
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
            }

            scheduledVersion = ++batch.Version;
        }

        _ = FlushAfterDelay(seriesKey, scheduledVersion, _window);
    }

    private async Task FlushAfterDelay(string key, int version, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);

            int count;
            string name;
            string? singleEpisodeId;
            string? singleEpisodeName;
            int? singleSeasonNumber;
            int? singleEpisodeNumber;
            lock (_gate)
            {
                if (!_batches.TryGetValue(key, out var current))
                {
                    return; // already flushed/replaced
                }

                if (current.Version != version)
                {
                    return; // superseded by a newer enqueue
                }

                count = current.Count;
                name = current.SeriesName;
                singleEpisodeId = current.SingleEpisodeId;
                singleEpisodeName = current.SingleEpisodeName;
                singleSeasonNumber = current.SingleSeasonNumber;
                singleEpisodeNumber = current.SingleEpisodeNumber;
                _batches.Remove(key);
            }

            if (count <= 0)
            {
                return;
            }

            // Build message and recipients at send time to respect latest preferences
            var users = JellyTVUserStore.FilterUsersForEvent(
                JellyTVUserStore.Load().Select(u => u.UserId),
                "ItemAdded");

            _logger.LogDebug(
                "Flushing episode batch: seriesKey={SeriesKey}; seriesName={SeriesName}; count={Count}; singleEpisodeId={EpisodeId}; season={Season}; episode={Episode}; users={UserCount}",
                key,
                name,
                count,
                singleEpisodeId,
                singleSeasonNumber,
                singleEpisodeNumber,
                users.Count);

            if (count == 1)
            {
                var friendlyBody = BuildEpisodeMessage(
                    name,
                    singleEpisodeName,
                    singleSeasonNumber,
                    singleEpisodeNumber);

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
                    string.IsNullOrWhiteSpace(name) ? singleEpisodeName : name,
                    bodyOverride: friendlyBody).ConfigureAwait(false);
                return;
            }

            string itemName = string.IsNullOrWhiteSpace(name)
                ? Localizer.Format("EpisodesNewNoSeries", new Dictionary<string, string> { ["Count"] = count.ToString(CultureInfo.InvariantCulture) })
                : (count == 1
                    ? Localizer.Format("EpisodeNewForSeries", new Dictionary<string, string> { ["Series"] = name })
                    : Localizer.Format("EpisodesNewForSeries", new Dictionary<string, string> { ["Series"] = name, ["Count"] = count.ToString(CultureInfo.InvariantCulture) }));

            _logger.LogInformation("Batch push: series={Series}; count={Count}; users={Users}", name, count, users.Count);
            await _pushService.SendEventAsync("ItemAdded", itemId: null, userIds: users, itemName: itemName).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while flushing episode batch: {Message}", ex.Message);
        }
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

            var friendlyBody = BuildEpisodeMessage(seriesName, episodeDisplayName, seasonNumber, episodeNumber);

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

    private static string BuildEpisodeMessage(string seriesName, string? fallbackName, int? seasonNumber, int? episodeNumber)
    {
        var hasSeries = !string.IsNullOrWhiteSpace(seriesName);
        if (hasSeries && seasonNumber.HasValue && episodeNumber.HasValue)
        {
            return Localizer.Format(
                "EpisodeNewForSeriesDetailed",
                new Dictionary<string, string>
                {
                    ["Series"] = seriesName,
                    ["Season"] = seasonNumber.Value.ToString(CultureInfo.InvariantCulture),
                    ["Episode"] = episodeNumber.Value.ToString(CultureInfo.InvariantCulture)
                });
        }

        if (!hasSeries && seasonNumber.HasValue && episodeNumber.HasValue)
        {
            return Localizer.Format(
                "EpisodeNewDetailedNoSeries",
                new Dictionary<string, string>
                {
                    ["Season"] = seasonNumber.Value.ToString(CultureInfo.InvariantCulture),
                    ["Episode"] = episodeNumber.Value.ToString(CultureInfo.InvariantCulture)
                });
        }

        if (hasSeries)
        {
            return Localizer.Format(
                "EpisodeNewForSeries",
                new Dictionary<string, string>
                {
                    ["Series"] = seriesName
                });
        }

        return string.IsNullOrWhiteSpace(fallbackName)
            ? Localizer.T("ItemAddedGeneric")
            : fallbackName!;
    }

    private sealed class Batch
    {
        public Batch(string seriesName)
        {
            SeriesName = seriesName;
        }

        public string SeriesName { get; set; }

        public int Count { get; set; }

        public int Version { get; set; }

        public string? SingleEpisodeId { get; set; }

        public string? SingleEpisodeName { get; set; }

        public int? SingleSeasonNumber { get; set; }

        public int? SingleEpisodeNumber { get; set; }
    }
}
