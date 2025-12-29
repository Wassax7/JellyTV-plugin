using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Simple in-memory rate limiting service using sliding window algorithm.
/// </summary>
public sealed class RateLimitService : IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitService"/> class.
    /// </summary>
    public RateLimitService()
    {
        _cleanupTimer = new Timer(CleanupExpiredBuckets, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Attempts to acquire a rate limit slot for the given key.
    /// </summary>
    /// <param name="key">The rate limit key (e.g., "register:userId").</param>
    /// <param name="maxRequests">Maximum requests allowed in the window.</param>
    /// <param name="window">The time window.</param>
    /// <returns>True if the request is allowed, false if rate limited.</returns>
    public bool TryAcquire(string key, int maxRequests, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new RateLimitBucket());
        return bucket.TryAddRequest(now, maxRequests, window);
    }

    /// <summary>
    /// Gets the number of seconds until the rate limit resets for a key.
    /// </summary>
    /// <param name="key">The rate limit key.</param>
    /// <param name="window">The time window.</param>
    /// <returns>Seconds until reset, or 0 if not rate limited.</returns>
    public int GetRetryAfterSeconds(string key, TimeSpan window)
    {
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            return 0;
        }

        var oldest = bucket.OldestRequest;
        if (!oldest.HasValue)
        {
            return 0;
        }

        var resetTime = oldest.Value.Add(window);
        var remaining = resetTime - DateTimeOffset.UtcNow;
        return remaining.TotalSeconds > 0 ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
    }

    private void CleanupExpiredBuckets(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var kvp in _buckets)
        {
            if (kvp.Value.LastActivity.HasValue &&
                now - kvp.Value.LastActivity.Value > TimeSpan.FromMinutes(2))
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _buckets.TryRemove(key, out _);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer.Dispose();
    }

    private sealed class RateLimitBucket
    {
        private readonly List<DateTimeOffset> _requests = new();
        private readonly object _lock = new();

        public int RequestCount
        {
            get
            {
                lock (_lock)
                {
                    return _requests.Count;
                }
            }
        }

        public DateTimeOffset? OldestRequest
        {
            get
            {
                lock (_lock)
                {
                    return _requests.Count > 0 ? _requests[0] : null;
                }
            }
        }

        public DateTimeOffset? LastActivity { get; private set; }

        public bool TryAddRequest(DateTimeOffset timestamp, int maxRequests, TimeSpan window)
        {
            lock (_lock)
            {
                var cutoff = timestamp - window;
                _requests.RemoveAll(t => t < cutoff);

                if (_requests.Count >= maxRequests)
                {
                    return false;
                }

                _requests.Add(timestamp);
                LastActivity = timestamp;
                return true;
            }
        }
    }
}
