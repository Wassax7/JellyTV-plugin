using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyTV.Configuration;
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
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null || !cfg.ForwardPlaybackStart)
        {
            return;
        }

        var actorId = e.Session?.UserId ?? Guid.Empty;
        var actorUid = actorId == Guid.Empty ? string.Empty : actorId.ToString("N");
        // Send to all registered users except the actor, honoring per-user preferences
        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId)
            .Where(uid => !string.Equals(uid, actorUid, StringComparison.OrdinalIgnoreCase));
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStart");

        var itemId = e.Item?.Id.ToString("N");
        var itemName = e.Item is Episode epStart ? (epStart.SeriesName ?? e.Item?.Name) : e.Item?.Name;
        var userName = e.Session?.UserName;
        _logger.LogDebug("PlaybackStart event: userIds={UserIds}; itemId={ItemId}; name={Name}; user={User}", string.Join(",", userIds), itemId, itemName, userName);
        await _pushService.SendEventAsync("PlaybackStart", itemId, userIds, itemName, userName).ConfigureAwait(false);
    }

    private async void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null || !cfg.ForwardPlaybackStop)
        {
            return;
        }

        var actorId = e.Session?.UserId ?? Guid.Empty;
        var actorUid = actorId == Guid.Empty ? string.Empty : actorId.ToString("N");
        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId)
            .Where(uid => !string.Equals(uid, actorUid, StringComparison.OrdinalIgnoreCase));
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStop");

        var itemId = e.Item?.Id.ToString("N");
        var itemName = e.Item is Episode epStop ? (epStop.SeriesName ?? e.Item?.Name) : e.Item?.Name;
        var userName = e.Session?.UserName;
        _logger.LogDebug("PlaybackStop event: userIds={UserIds}; itemId={ItemId}; name={Name}; user={User}", string.Join(",", userIds), itemId, itemName, userName);
        await _pushService.SendEventAsync("PlaybackStop", itemId, userIds, itemName, userName).ConfigureAwait(false);
    }

    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg == null || !cfg.ForwardItemAdded)
        {
            return;
        }

        // Filter: only forward for Movies, Series, and Episodes to avoid People/Artist/etc.
        var item = e.Item as BaseItem;
        if (item is null || !(item is Movie || item is Series || item is Episode))
        {
            return;
        }

        if (item is Episode ep)
        {
            // Batch per series within a short window.
            var seriesKey = (ep.SeriesId != Guid.Empty ? ep.SeriesId.ToString("N") : ep.SeriesName) ?? string.Empty;
            var seriesName = ep.SeriesName ?? string.Empty;
            _episodeBatcher.Enqueue(seriesKey, seriesName);
            return;
        }

        var itemId = item.Id.ToString("N");
        var itemName = GetDisplayName(item);
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(
            Services.JellyTVUserStore.Load().Select(u => u.UserId),
            "ItemAdded");
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
}
