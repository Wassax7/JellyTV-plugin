using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyTV.Localization;
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
    private readonly object _libraryItemsGate = new object();
    private readonly object _pendingItemsGate = new object();
    private readonly object _backgroundOperationsGate = new object();
    private readonly Dictionary<string, DateTimeOffset> _recentPlaybackStarts = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownLibraryItemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, PendingItemAdded> _pendingItems = new Dictionary<Guid, PendingItemAdded>();
    private readonly HashSet<Task> _backgroundOperations = new HashSet<Task>();
    private readonly TimeSpan _playbackStartCooldown = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _pendingItemLifetime = TimeSpan.FromMinutes(2);
    private bool _acceptingPendingItems;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _pendingCleanupTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTVEventListener"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="pushService">The push service used to send notifications.</param>
    /// <param name="episodeBatcher">The batcher that aggregates closely-timed episode additions.</param>
    /// <param name="tokenEncryption">The token encryption service for securing stored tokens.</param>
    public JellyTVEventListener(
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        ILogger<JellyTVEventListener> logger,
        JellyTVPushService pushService,
        JellyTVEpisodeBatcher episodeBatcher,
        JellyTVTokenEncryption tokenEncryption)
    {
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _pushService = pushService;
        _episodeBatcher = episodeBatcher;

        // Initialize the static user store with the encryption service
        JellyTVUserStore.SetEncryptionService(tokenEncryption);
    }

    /// <inheritdoc />
    public Task StartAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyTV listener starting; subscribing to events.");
        InitializeKnownLibraryItems();
        var lifetimeCancellation = new CancellationTokenSource();
        lock (_backgroundOperationsGate)
        {
            _lifetimeCancellation = lifetimeCancellation;
        }

        lock (_pendingItemsGate)
        {
            _acceptingPendingItems = true;
        }

        _episodeBatcher.Start();
        _pendingCleanupTask = CleanupExpiredPendingItemsAsync(lifetimeCancellation.Token);
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(System.Threading.CancellationToken cancellationToken)
    {
        _logger.LogInformation("JellyTV listener stopping; unsubscribing from events.");
        lock (_pendingItemsGate)
        {
            _acceptingPendingItems = false;
        }

        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;

        CancellationTokenSource? lifetimeCancellation;
        Task[] backgroundOperations;
        lock (_backgroundOperationsGate)
        {
            lifetimeCancellation = _lifetimeCancellation;
            _lifetimeCancellation = null;
            backgroundOperations = _backgroundOperations.ToArray();
        }

        if (lifetimeCancellation is not null)
        {
            await lifetimeCancellation.CancelAsync().ConfigureAwait(false);
        }

        await _episodeBatcher.StopAsync(cancellationToken).ConfigureAwait(false);

        var cleanupTask = _pendingCleanupTask;
        _pendingCleanupTask = null;
        if (cleanupTask is not null)
        {
            try
            {
                await cleanupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the listener or host is stopping.
            }
        }

        lock (_pendingItemsGate)
        {
            _pendingItems.Clear();
        }

        if (backgroundOperations.Length > 0)
        {
            try
            {
                await Task.WhenAll(backgroundOperations).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The host controls how long shutdown may wait for in-flight sends.
            }
        }

        lifetimeCancellation?.Dispose();
    }

    /// <summary>
    /// Disposes the listener.
    /// </summary>
    public void Dispose()
    {
        lock (_pendingItemsGate)
        {
            _acceptingPendingItems = false;
            _pendingItems.Clear();
        }

        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;

        CancellationTokenSource? lifetimeCancellation;
        lock (_backgroundOperationsGate)
        {
            lifetimeCancellation = _lifetimeCancellation;
            _lifetimeCancellation = null;
        }

        lifetimeCancellation?.Cancel();
        _episodeBatcher.CancelPending();
        lifetimeCancellation?.Dispose();
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

        // Send to all registered users except the actor, honoring per-user preferences
        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId);
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStart");

        // Exclude the user who triggered the playback from receiving the notification
        if (!string.IsNullOrEmpty(actorUid))
        {
            userIds = userIds.Where(uid => !string.Equals(uid, actorUid, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (userIds.Count == 0)
        {
            _logger.LogDebug("PlaybackStart event skipped: no opted-in recipients (excluding actor).");
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

        // Get the actor's user ID to exclude them from notifications
        var actorId = e.Session?.UserId ?? Guid.Empty;
        var actorUid = actorId == Guid.Empty ? string.Empty : actorId.ToString("N");

        var candidateIds = Services.JellyTVUserStore.Load()
            .Select(u => u.UserId);
        var userIds = Services.JellyTVUserStore.FilterUsersForEvent(candidateIds, "PlaybackStop");

        // Exclude the user who triggered the playback stop from receiving the notification
        if (!string.IsNullOrEmpty(actorUid))
        {
            userIds = userIds.Where(uid => !string.Equals(uid, actorUid, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (userIds.Count == 0)
        {
            _logger.LogDebug("PlaybackStop event skipped: no opted-in recipients (excluding actor).");
            return;
        }

        var itemId = e.Item?.Id.ToString("N");
        var itemName = e.Item is Episode epStop ? (epStop.SeriesName ?? e.Item?.Name) : e.Item?.Name;
        var userName = e.Session?.UserName;
        _logger.LogDebug("PlaybackStop event: userIds={UserIds}; itemId={ItemId}; name={Name}; user={User}", string.Join(",", userIds), itemId, itemName, userName);
        await _pushService.SendEventAsync("PlaybackStop", itemId, userIds, itemName, userName).ConfigureAwait(false);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var plugin = Plugin.Instance;
            if (plugin?.Configuration?.ForwardItemAdded != true)
            {
                return;
            }

            // Filter: only forward for Movies, Series, and Episodes to avoid People/Artist/etc.
            var item = e.Item as BaseItem;
            if (item is not Movie && item is not Series && item is not Episode)
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

            QueuePendingItem(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while queueing ItemAdded notification: {Message}", ex.Message);
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            PendingItemAdded? pendingItem = null;
            var typeChanged = false;

            lock (_pendingItemsGate)
            {
                if (!_pendingItems.TryGetValue(e.Item.Id, out var currentPendingItem))
                {
                    return;
                }

                if (!HasMatchingNotificationType(currentPendingItem.InitialItem, e.Item))
                {
                    _pendingItems.Remove(e.Item.Id);
                    typeChanged = true;
                }
                else
                {
                    var refreshCompleted = e.Item.DateLastRefreshed > currentPendingItem.InitialDateLastRefreshed;
                    if (refreshCompleted)
                    {
                        _pendingItems.Remove(e.Item.Id);
                        pendingItem = currentPendingItem;
                    }
                }
            }

            if (typeChanged)
            {
                _logger.LogWarning(
                    "Pending ItemAdded changed type before metadata completed: itemId={ItemId}; type={Type}",
                    e.Item.Id.ToString("N"),
                    e.Item.GetType().Name);
                return;
            }

            if (pendingItem is not null)
            {
                RememberLibraryItem(e.Item);
                RunBackgroundOperation(() => ProcessPendingItemAsync(pendingItem, e.Item, false));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing ItemUpdated notification: {Message}", ex.Message);
        }
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        lock (_pendingItemsGate)
        {
            _pendingItems.Remove(e.Item.Id);
        }
    }

    private void QueuePendingItem(BaseItem item)
    {
        if (item.Id == Guid.Empty)
        {
            _logger.LogWarning("ItemAdded notification skipped because the item has no identifier. name={Name}", item.Name ?? string.Empty);
            return;
        }

        lock (_pendingItemsGate)
        {
            if (!_acceptingPendingItems)
            {
                return;
            }

            if (_pendingItems.ContainsKey(item.Id))
            {
                _logger.LogDebug("Duplicate ItemAdded event ignored while metadata is pending: itemId={ItemId}", item.Id.ToString("N"));
                return;
            }

            var wasAlreadyInLibrary = ObserveLibraryItem(item);
            _pendingItems[item.Id] = new PendingItemAdded(
                item,
                item.DateLastRefreshed,
                DateTimeOffset.UtcNow.Add(_pendingItemLifetime),
                wasAlreadyInLibrary);
        }

        _logger.LogDebug(
            "ItemAdded queued until metadata update: itemId={ItemId}; type={Type}; initialRefresh={InitialRefresh}",
            item.Id.ToString("N"),
            item.GetType().Name,
            item.DateLastRefreshed);
    }

    private async Task CleanupExpiredPendingItemsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                List<PendingItemAdded> expiredItems;
                var now = DateTimeOffset.UtcNow;

                lock (_pendingItemsGate)
                {
                    expiredItems = _pendingItems.Values
                        .Where(item => item.ExpiresAt <= now)
                        .ToList();

                    foreach (var expiredItem in expiredItems)
                    {
                        _pendingItems.Remove(expiredItem.InitialItem.Id);
                    }
                }

                foreach (var expiredItem in expiredItems)
                {
                    try
                    {
                        var latestItem = _libraryManager.GetItemById(expiredItem.InitialItem.Id);
                        if (latestItem is null)
                        {
                            _logger.LogDebug(
                                "Expired ItemAdded notification dropped because the item no longer exists: itemId={ItemId}",
                                expiredItem.InitialItem.Id.ToString("N"));
                            continue;
                        }

                        if (!HasMatchingNotificationType(expiredItem.InitialItem, latestItem))
                        {
                            _logger.LogWarning(
                                "Expired ItemAdded notification dropped because the item changed type: itemId={ItemId}; type={Type}",
                                expiredItem.InitialItem.Id.ToString("N"),
                                latestItem.GetType().Name);
                            continue;
                        }

                        RememberLibraryItem(latestItem);
                        RunBackgroundOperation(() => ProcessPendingItemAsync(expiredItem, latestItem, true));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error while expiring pending ItemAdded notification: itemId={ItemId}; message={Message}",
                            expiredItem.InitialItem.Id.ToString("N"),
                            ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the listener stops.
        }
    }

    private async Task ProcessPendingItemAsync(PendingItemAdded pendingItem, BaseItem item, bool timedOut)
    {
        var currentItem = _libraryManager.GetItemById(item.Id);
        if (currentItem is null)
        {
            _logger.LogDebug("ItemAdded notification dropped because the item no longer exists: itemId={ItemId}", item.Id.ToString("N"));
            return;
        }

        if (!HasMatchingNotificationType(pendingItem.InitialItem, currentItem))
        {
            _logger.LogWarning(
                "ItemAdded notification dropped because the item changed type: itemId={ItemId}; type={Type}",
                item.Id.ToString("N"),
                currentItem.GetType().Name);
            return;
        }

        item = currentItem;
        if (timedOut)
        {
            _logger.LogWarning(
                "Metadata completion was not observed before timeout; using latest available item name. itemId={ItemId}; name={Name}",
                item.Id.ToString("N"),
                item.Name ?? string.Empty);
        }

        if (pendingItem.WasAlreadyInLibrary)
        {
            await SendUpdatedNotificationAsync(item).ConfigureAwait(false);
            return;
        }

        if (item is Episode episode)
        {
            var identity = ResolveEpisodeBatchIdentity(episode);
            _logger.LogDebug(
                "Queueing episode ItemAdded: episodeId={EpisodeId}; seriesKey={SeriesKey}; seriesName={SeriesName}; displayName={DisplayName}; season={Season}; episode={Episode}",
                identity.EpisodeId,
                identity.SeriesKey,
                identity.SeriesName,
                identity.EpisodeDisplayName,
                identity.SeasonNumber,
                identity.EpisodeNumber);
            _episodeBatcher.Enqueue(
                identity.SeriesId,
                identity.SeriesKey,
                identity.SeriesName,
                identity.EpisodeId,
                identity.EpisodeDisplayName,
                identity.SeasonNumber,
                identity.EpisodeNumber);
            return;
        }

        var userIds = JellyTVUserStore.FilterUsersForEvent(
            JellyTVUserStore.Load().Select(user => user.UserId),
            "ItemAdded");
        var itemId = item.Id.ToString("N");
        var itemName = GetDisplayName(item);
        _logger.LogDebug("ItemAdded metadata ready: users={UserCount}; itemId={ItemId}; name={Name}", userIds.Count, itemId, itemName);
        await _pushService.SendEventAsync("ItemAdded", itemId, userIds, itemName).ConfigureAwait(false);
    }

    private void RunBackgroundOperation(Func<Task> operation)
    {
        Task task;
        lock (_backgroundOperationsGate)
        {
            var lifetimeCancellation = _lifetimeCancellation;
            if (lifetimeCancellation is null || lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            task = ExecuteBackgroundOperationAsync(operation, lifetimeCancellation.Token);
            _backgroundOperations.Add(task);
        }

        _ = ObserveBackgroundOperationAsync(task);
    }

    private async Task ExecuteBackgroundOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending library event notification: {Message}", ex.Message);
        }
    }

    private async Task ObserveBackgroundOperationAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        finally
        {
            lock (_backgroundOperationsGate)
            {
                _backgroundOperations.Remove(operation);
            }
        }
    }

    private void RememberLibraryItem(BaseItem item)
    {
        lock (_libraryItemsGate)
        {
            AddLibraryItemKeys(item);
        }
    }

    private static bool HasMatchingNotificationType(BaseItem initialItem, BaseItem currentItem)
        => (initialItem is Movie && currentItem is Movie)
            || (initialItem is Series && currentItem is Series)
            || (initialItem is Episode && currentItem is Episode);

    private void InitializeKnownLibraryItems()
    {
        try
        {
            var existingItems = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                Recursive = true
            });

            lock (_libraryItemsGate)
            {
                foreach (var existingItem in existingItems)
                {
                    AddLibraryItemKeys(existingItem);
                }
            }

            _logger.LogDebug("Initialized known library item identities: count={Count}", existingItems.Count);
        }
        catch (Exception ex)
        {
            // ItemAdded notifications remain available if the initial library snapshot cannot be read.
            _logger.LogWarning(ex, "Unable to initialize known library item identities; update detection will start with future events.");
        }
    }

    private bool ObserveLibraryItem(BaseItem item)
    {
        var keys = GetLibraryItemKeys(item).ToList();
        if (keys.Count == 0)
        {
            return false;
        }

        lock (_libraryItemsGate)
        {
            var wasAlreadyKnown = keys.Any(key => _knownLibraryItemKeys.Contains(key));
            foreach (var key in keys)
            {
                _knownLibraryItemKeys.Add(key);
            }

            return wasAlreadyKnown;
        }
    }

    private void AddLibraryItemKeys(BaseItem item)
    {
        foreach (var key in GetLibraryItemKeys(item))
        {
            _knownLibraryItemKeys.Add(key);
        }
    }

    private static IEnumerable<string> GetLibraryItemKeys(BaseItem item)
    {
        if (item.Id != Guid.Empty)
        {
            yield return $"id:{item.Id:N}";
        }

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            var path = item.Path.Trim();
            yield return $"path:{path}";

            // Movie files can be replaced with a different quality filename while remaining in the same folder.
            if (item is Movie)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    yield return $"movie-directory:{directory}";
                }
            }
        }

        foreach (var providerId in item.ProviderIds)
        {
            if (!string.IsNullOrWhiteSpace(providerId.Key) && !string.IsNullOrWhiteSpace(providerId.Value))
            {
                yield return $"provider:{providerId.Key}:{providerId.Value}";
            }
        }

        if (item is Episode episode && episode.SeriesId != Guid.Empty && episode.ParentIndexNumber.HasValue && episode.IndexNumber.HasValue)
        {
            yield return $"episode:{episode.SeriesId:N}:{episode.ParentIndexNumber.Value}:{episode.IndexNumber.Value}";
        }
    }

    private async Task SendUpdatedNotificationAsync(BaseItem item)
    {
        var itemId = item.Id.ToString("N");
        var itemName = GetDisplayName(item);
        _logger.LogDebug("ItemAdded identified an existing library item as updated: itemId={ItemId}; name={Name}", itemId, itemName);
        await _pushService.SendEventAsync(
            "ItemAdded",
            itemId,
            JellyTVUserStore.FilterUsersForEvent(JellyTVUserStore.Load().Select(u => u.UserId), "ItemAdded"),
            itemName,
            bodyOverride: Localizer.Format("ItemUpdatedNamed", new Dictionary<string, string> { ["Item"] = itemName })).ConfigureAwait(false);
    }

    private string GetDisplayName(BaseItem item)
    {
        if (item is Episode ep)
        {
            var series = GetCurrentSeriesName(ep) ?? string.Empty;
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

    private EpisodeBatchIdentity ResolveEpisodeBatchIdentity(Episode episode)
    {
        var seriesId = episode.SeriesId;
        string key;

        // Primary: Use episode.SeriesId if populated
        if (seriesId != Guid.Empty)
        {
            key = seriesId.ToString("N");
        }
        else
        {
            // Secondary: Call FindSeriesId() to navigate parent hierarchy
            var foundSeriesId = episode.FindSeriesId();
            if (foundSeriesId != Guid.Empty)
            {
                seriesId = foundSeriesId;
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

        // Prefer the current parent Series.Name because Episode.SeriesName may be a stale snapshot
        // captured before the series metadata was localized.
        var seriesName = GetCurrentSeriesName(episode);

        if (string.IsNullOrWhiteSpace(seriesName))
        {
            // Fallback chain: Season.SeriesName → FindSeriesName() → Season.Name
            var season = episode.Season;
            if (!string.IsNullOrWhiteSpace(season?.SeriesName) && !LooksLikeFileName(season.SeriesName))
            {
                seriesName = season.SeriesName;
            }
            else
            {
                var foundSeriesName = episode.FindSeriesName();
                if (!string.IsNullOrWhiteSpace(foundSeriesName) && !LooksLikeFileName(foundSeriesName))
                {
                    seriesName = foundSeriesName;
                }
                else if (!string.IsNullOrWhiteSpace(season?.Name))
                {
                    seriesName = season.Name;
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
            seriesId,
            key,
            seriesName ?? string.Empty,
            episodeId,
            displayName,
            seasonNumber,
            episodeNumber);
    }

    private string? GetCurrentSeriesName(Episode episode)
    {
        var series = episode.Series;
        if (series is null && episode.SeriesId != Guid.Empty)
        {
            series = _libraryManager.GetItemById(episode.SeriesId) as Series;
        }

        if (!string.IsNullOrWhiteSpace(series?.Name) && !LooksLikeFileName(series.Name))
        {
            return series.Name;
        }

        return !string.IsNullOrWhiteSpace(episode.SeriesName) && !LooksLikeFileName(episode.SeriesName)
            ? episode.SeriesName
            : null;
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
        Guid SeriesId,
        string SeriesKey,
        string SeriesName,
        string EpisodeId,
        string EpisodeDisplayName,
        int? SeasonNumber,
        int? EpisodeNumber);

    private sealed class PendingItemAdded
    {
        public PendingItemAdded(
            BaseItem initialItem,
            DateTime initialDateLastRefreshed,
            DateTimeOffset expiresAt,
            bool wasAlreadyInLibrary)
        {
            InitialItem = initialItem;
            InitialDateLastRefreshed = initialDateLastRefreshed;
            ExpiresAt = expiresAt;
            WasAlreadyInLibrary = wasAlreadyInLibrary;
        }

        public BaseItem InitialItem { get; }

        public DateTime InitialDateLastRefreshed { get; }

        public DateTimeOffset ExpiresAt { get; }

        public bool WasAlreadyInLibrary { get; }
    }
}
