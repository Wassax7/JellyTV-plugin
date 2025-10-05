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
    public void Enqueue(string seriesKey, string seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            return;
        }

        lock (_gate)
        {
            if (!_batches.TryGetValue(seriesKey, out var batch))
            {
                batch = new Batch(seriesName);
                _batches[seriesKey] = batch;
                // Schedule flush
                _ = FlushAfterDelay(seriesKey, batch, _window);
            }

            batch.Count++;
            batch.SeriesName = string.IsNullOrWhiteSpace(seriesName) ? batch.SeriesName : seriesName;
        }
    }

    private async Task FlushAfterDelay(string key, Batch batch, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay).ConfigureAwait(false);

            int count;
            string name;
            lock (_gate)
            {
                if (!_batches.TryGetValue(key, out var current) || !ReferenceEquals(current, batch))
                {
                    return; // already flushed/replaced
                }

                count = current.Count;
                name = current.SeriesName;
                _batches.Remove(key);
            }

            if (count <= 0)
            {
                return;
            }

            // Build message and recipients at send time to respect latest preferences
            var users = JellyTVUserStore.Load()
                .Select(u => u.UserId)
                .Where(uid => JellyTVUserStore.IsEventAllowedForUser(uid, "ItemAdded"))
                .ToList();

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

    private sealed class Batch
    {
        public Batch(string seriesName)
        {
            SeriesName = seriesName;
        }

        public string SeriesName { get; set; }

        public int Count { get; set; }
    }
}
