using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyTV.Configuration;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Persistent store for user push tokens, independent from plugin config.
/// </summary>
internal static class JellyTVUserStore
{
    private const string FileName = "registered-users.json";
    private static Dictionary<string, JellyTVUserPreferences>? _preferencesCache;

    private static string NormalizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        return Guid.TryParse(userId, out var guid)
            ? guid.ToString("N")
            : userId.Trim().ToLowerInvariant();
    }

    private static string GetStorePath()
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("Plugin not initialized");
        // Use the plugin's data directory to persist across restarts and upgrades.
        var dir = plugin.DataDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            dir = AppContext.BaseDirectory;
        }

        return Path.Combine(dir!, FileName);
    }

    public static List<JellyTVUserTokens> Load()
    {
        try
        {
            var path = GetStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // One-time migration: if no store file yet, but legacy config has entries, migrate them.
            if (!File.Exists(path))
            {
                var cfg = Plugin.Instance?.Configuration;
                if (cfg != null && cfg.RegisteredUsers.Count > 0)
                {
                    var migrated = cfg.RegisteredUsers.Select(u => new JellyTVUserTokens
                    {
                        UserId = NormalizeUserId(u.UserId)
                    }).ToList();
                    // Manually copy tokens as Collection<string>
                    for (int i = 0; i < migrated.Count; i++)
                    {
                        foreach (var t in cfg.RegisteredUsers[i].Tokens)
                        {
                            if (!string.IsNullOrWhiteSpace(t) && !migrated[i].Tokens.Any(existing => string.Equals(existing, t, StringComparison.OrdinalIgnoreCase)))
                            {
                                migrated[i].Tokens.Add(t);
                            }
                        }
                    }

                    Save(migrated);
                    try
                    {
                        // Clear legacy config to stop future writes/reads there
                        cfg.RegisteredUsers.Clear();
                        Plugin.Instance!.UpdateConfiguration(cfg);
                    }
                    catch
                    {
                        // ignore
                    }

                    return migrated;
                }

                return new List<JellyTVUserTokens>();
            }

            var json = File.ReadAllText(path);
            var records = JsonSerializer.Deserialize<List<UserStoreRecord>>(json) ?? new List<UserStoreRecord>();
            var map = new Dictionary<string, JellyTVUserTokens>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                var normalizedId = NormalizeUserId(r.UserId);
                if (string.IsNullOrWhiteSpace(normalizedId))
                {
                    continue;
                }

                if (!map.TryGetValue(normalizedId, out var user))
                {
                    user = new JellyTVUserTokens { UserId = normalizedId };
                    map[normalizedId] = user;
                }

                if (r.Tokens != null)
                {
                    foreach (var t in r.Tokens)
                    {
                        if (!string.IsNullOrWhiteSpace(t) && !user.Tokens.Any(existing => string.Equals(existing, t, StringComparison.Ordinal)))
                        {
                            user.Tokens.Add(t);
                        }
                    }
                }
            }

            return map.Values.ToList();
        }
        catch
        {
            return new List<JellyTVUserTokens>();
        }
    }

    private static void Save(List<JellyTVUserTokens> users)
    {
        try
        {
            var path = GetStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var recordsMap = new Dictionary<string, UserStoreRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
            {
                var normalizedId = NormalizeUserId(user.UserId);
                if (string.IsNullOrWhiteSpace(normalizedId))
                {
                    continue;
                }

                if (!recordsMap.TryGetValue(normalizedId, out var record))
                {
                    record = new UserStoreRecord
                    {
                        UserId = normalizedId,
                        Tokens = new List<string>()
                    };
                    recordsMap[normalizedId] = record;
                }

                if (user.Tokens != null)
                {
                    foreach (var token in user.Tokens)
                    {
                        if (!string.IsNullOrWhiteSpace(token) && !record.Tokens.Any(existing => string.Equals(existing, token, StringComparison.OrdinalIgnoreCase)))
                        {
                            record.Tokens.Add(token);
                        }
                    }
                }

                record.Preferences = ToRecord(GetPreferences(normalizedId));
            }

            var json = JsonSerializer.Serialize(recordsMap.Values);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    public static (JellyTVUserTokens User, bool IsNewToken) UpsertToken(string userId, string token)
    {
        var normalizedId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            throw new ArgumentException("userId must be a valid Jellyfin user identifier.", nameof(userId));
        }

        var users = Load();
        var user = users.FirstOrDefault(u => string.Equals(u.UserId, normalizedId, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            user = new JellyTVUserTokens { UserId = normalizedId };
            users.Add(user);
        }

        // Replace-on-register: keep only the latest token for this user.
        // If the same token is already the only one, treat as not new and do nothing.
        var exists = user.Tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
        var isNew = false;
        if (user.Tokens.Count == 1 && exists)
        {
            // No change needed; exact same single token present.
            Save(users);
            return (user, false);
        }

        // Otherwise, replace the set with only this token.
        user.Tokens.Clear();
        user.Tokens.Add(token);
        isNew = !exists; // only consider it "new" if it wasn't already present

        Save(users);
        return (user, isNew);
    }

    // In-memory preferences cache to avoid changing JellyTVUserTokens data shape.

    private static Dictionary<string, JellyTVUserPreferences> LoadPreferences()
    {
        try
        {
            var path = GetStorePath();
            if (!File.Exists(path))
            {
                return new Dictionary<string, JellyTVUserPreferences>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(path);
            var records = JsonSerializer.Deserialize<List<UserStoreRecord>>(json) ?? new List<UserStoreRecord>();
            var dict = new Dictionary<string, JellyTVUserPreferences>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                var prefs = FromRecord(r.Preferences);
                var normalizedId = NormalizeUserId(r.UserId);
                if (prefs != null && !string.IsNullOrWhiteSpace(normalizedId))
                {
                    dict[normalizedId] = prefs;
                }
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, JellyTVUserPreferences>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void EnsurePrefsLoaded()
    {
        _preferencesCache ??= LoadPreferences();
    }

    public static void SetPreferences(string userId, JellyTVUserPreferences prefs)
    {
        EnsurePrefsLoaded();
        var normalizedId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        var normalizedPrefs = new JellyTVUserPreferences
        {
            ForwardItemAdded = prefs.ForwardItemAdded,
            ForwardPlaybackStart = prefs.ForwardPlaybackStart,
            ForwardPlaybackStop = prefs.ForwardPlaybackStop
        };

        var hasPreferences = normalizedPrefs.ForwardItemAdded.HasValue
            || normalizedPrefs.ForwardPlaybackStart.HasValue
            || normalizedPrefs.ForwardPlaybackStop.HasValue;

        if (hasPreferences)
        {
            _preferencesCache![normalizedId] = normalizedPrefs;
        }
        else
        {
            _preferencesCache!.Remove(normalizedId);
        }

        // Persist by re-saving the user store (tokens + prefs)
        var users = Load();
        // Make sure the user exists even if no token yet
        if (hasPreferences && !users.Any(u => string.Equals(u.UserId, normalizedId, StringComparison.OrdinalIgnoreCase)))
        {
            users.Add(new JellyTVUserTokens { UserId = normalizedId });
        }

        Save(users);
    }

    public static JellyTVUserPreferences? GetPreferences(string userId)
    {
        EnsurePrefsLoaded();
        var normalizedId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return null;
        }

        return _preferencesCache!.TryGetValue(normalizedId, out var p) ? p : null;
    }

    public static bool IsEventAllowedForUser(string userId, string eventName)
    {
        var cfg = Plugin.Instance?.Configuration;

        var adminEnabled = eventName switch
        {
            "ItemAdded" => cfg?.ForwardItemAdded == true,
            "PlaybackStart" => cfg?.ForwardPlaybackStart == true,
            "PlaybackStop" => cfg?.ForwardPlaybackStop == true,
            _ => true
        };

        if (!adminEnabled)
        {
            return false;
        }

        var normalizedId = NormalizeUserId(userId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        var defaultAllow = adminEnabled;

        var prefs = GetPreferences(normalizedId);
        if (prefs == null)
        {
            return defaultAllow;
        }

        return eventName switch
        {
            "ItemAdded" => prefs.ForwardItemAdded.HasValue ? prefs.ForwardItemAdded.Value : defaultAllow,
            "PlaybackStart" => prefs.ForwardPlaybackStart.HasValue ? prefs.ForwardPlaybackStart.Value : defaultAllow,
            "PlaybackStop" => prefs.ForwardPlaybackStop.HasValue ? prefs.ForwardPlaybackStop.Value : defaultAllow,
            _ => defaultAllow
        };
    }

    public static int RemoveToken(string token)
    {
        var users = Load();
        var removed = 0;
        foreach (var u in users)
        {
            var before = u.Tokens.Count;
            var remaining = u.Tokens.Where(t => !string.Equals(t, token, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count != u.Tokens.Count)
            {
                u.Tokens.Clear();
                foreach (var t in remaining)
                {
                    u.Tokens.Add(t);
                }
            }

            removed += before - remaining.Count;
        }

        if (removed > 0)
        {
            Save(users);
        }

        return removed;
    }

    public static List<string> FilterUsersForEvent(IEnumerable<string> userIds, string eventName)
    {
        var result = new List<string>();
        if (userIds == null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds)
        {
            var normalizedId = NormalizeUserId(userId);
            if (string.IsNullOrWhiteSpace(normalizedId) || !seen.Add(normalizedId))
            {
                continue;
            }

            if (IsEventAllowedForUser(normalizedId, eventName))
            {
                result.Add(normalizedId);
            }
        }

        return result;
    }

    public static List<string> GetTokensForUsers(IEnumerable<string> userIds)
    {
        var users = Load();
        var normalizedSet = new HashSet<string>(
            (userIds ?? Array.Empty<string>())
                .Select(NormalizeUserId)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
        return users.Where(u => normalizedSet.Contains(u.UserId))
            .SelectMany(u => u.Tokens)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static UserPreferencesRecord? ToRecord(JellyTVUserPreferences? prefs)
    {
        if (prefs == null)
        {
            return null;
        }

        var hasValue = prefs.ForwardItemAdded.HasValue
            || prefs.ForwardPlaybackStart.HasValue
            || prefs.ForwardPlaybackStop.HasValue;
        if (!hasValue)
        {
            return null;
        }

        return new UserPreferencesRecord
        {
            ForwardItemAdded = prefs.ForwardItemAdded,
            ForwardPlaybackStart = prefs.ForwardPlaybackStart,
            ForwardPlaybackStop = prefs.ForwardPlaybackStop
        };
    }

    private static JellyTVUserPreferences? FromRecord(UserPreferencesRecord? rec)
    {
        if (rec == null)
        {
            return null;
        }

        var prefs = new JellyTVUserPreferences
        {
            ForwardItemAdded = rec.ForwardItemAdded,
            ForwardPlaybackStart = rec.ForwardPlaybackStart,
            ForwardPlaybackStop = rec.ForwardPlaybackStop
        };

        if (!prefs.ForwardPlaybackStart.HasValue || !prefs.ForwardPlaybackStop.HasValue)
        {
            if (rec.ForwardPlaybackStart.HasValue || rec.ForwardPlaybackStop.HasValue)
            {
                prefs.ForwardPlaybackStart ??= rec.ForwardPlaybackStart;
                prefs.ForwardPlaybackStop ??= rec.ForwardPlaybackStop;
            }
        }

        if (!prefs.ForwardItemAdded.HasValue && !prefs.ForwardPlaybackStart.HasValue && !prefs.ForwardPlaybackStop.HasValue)
        {
            return null;
        }

        return prefs;
    }

    private sealed class UserStoreRecord
    {
        public string UserId { get; set; } = string.Empty;

        public List<string> Tokens { get; set; } = new List<string>();

        public UserPreferencesRecord? Preferences { get; set; }
    }

    private sealed class UserPreferencesRecord
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ForwardItemAdded { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ForwardPlaybackStart { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ForwardPlaybackStop { get; set; }
    }
}
