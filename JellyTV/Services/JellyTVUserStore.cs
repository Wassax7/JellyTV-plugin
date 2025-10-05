using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.JellyTV.Configuration;

namespace Jellyfin.Plugin.JellyTV.Services;

/// <summary>
/// Persistent store for user push tokens, independent from plugin config.
/// </summary>
internal static class JellyTVUserStore
{
    private const string FileName = "registered-users.json";
    private static Dictionary<string, JellyTVUserPreferences>? _preferencesCache;

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
                        UserId = u.UserId
                    }).ToList();
                    // Manually copy tokens as Collection<string>
                    for (int i = 0; i < migrated.Count; i++)
                    {
                        foreach (var t in cfg.RegisteredUsers[i].Tokens)
                        {
                            migrated[i].Tokens.Add(t);
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
            var users = new List<JellyTVUserTokens>(records.Count);
            foreach (var r in records)
            {
                var u = new JellyTVUserTokens { UserId = r.UserId };
                foreach (var t in r.Tokens)
                {
                    u.Tokens.Add(t);
                }

                users.Add(u);
            }

            return users;
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
            // Map tokens and preferences; preferences resolved via separate map
            var prefMap = _preferencesCache; // may be null
            var records = users.Select(u => new UserStoreRecord
            {
                UserId = u.UserId,
                Tokens = u.Tokens.ToList(),
                Preferences = ToRecord(GetPreferences(u.UserId))
            }).ToList();

            var json = JsonSerializer.Serialize(records);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    public static (JellyTVUserTokens User, bool IsNewToken) UpsertToken(string userId, string token)
    {
        var users = Load();
        var user = users.FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase));
        if (user == null)
        {
            user = new JellyTVUserTokens { UserId = userId };
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
                if (prefs != null)
                {
                    dict[r.UserId] = prefs;
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
        _preferencesCache![userId] = prefs;

        // Persist by re-saving the user store (tokens + prefs)
        var users = Load();
        // Make sure the user exists even if no token yet
        if (!users.Any(u => string.Equals(u.UserId, userId, StringComparison.OrdinalIgnoreCase)))
        {
            users.Add(new JellyTVUserTokens { UserId = userId });
        }

        Save(users);
    }

    public static JellyTVUserPreferences? GetPreferences(string userId)
    {
        EnsurePrefsLoaded();
        return _preferencesCache!.TryGetValue(userId, out var p) ? p : null;
    }

    public static bool IsEventAllowedForUser(string userId, string eventName)
    {
        var prefs = GetPreferences(userId);
        if (prefs == null)
        {
            return true; // default allow
        }

        return eventName switch
        {
            "ItemAdded" => prefs.ForwardItemAdded != false,
            "PlaybackStart" => prefs.ForwardPlayback != false,
            "PlaybackStop" => prefs.ForwardPlayback != false,
            _ => true
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

    public static List<string> GetTokensForUsers(IEnumerable<string> userIds)
    {
        var users = Load();
        var set = new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);
        return users.Where(u => set.Contains(u.UserId))
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

        return new UserPreferencesRecord
        {
            ForwardItemAdded = prefs.ForwardItemAdded,
            ForwardPlayback = prefs.ForwardPlayback
        };
    }

    private static JellyTVUserPreferences? FromRecord(UserPreferencesRecord? rec)
    {
        if (rec == null)
        {
            return null;
        }

        bool? playback = rec.ForwardPlayback;
        if (playback == null && (rec.ForwardPlaybackStart != null || rec.ForwardPlaybackStop != null))
        {
            if (rec.ForwardPlaybackStart == false || rec.ForwardPlaybackStop == false)
            {
                playback = false;
            }
            else if (rec.ForwardPlaybackStart == true || rec.ForwardPlaybackStop == true)
            {
                playback = true;
            }
            else
            {
                playback = null;
            }
        }

        return new JellyTVUserPreferences
        {
            ForwardItemAdded = rec.ForwardItemAdded,
            ForwardPlayback = playback
        };
    }

    private sealed class UserStoreRecord
    {
        public string UserId { get; set; } = string.Empty;

        public List<string> Tokens { get; set; } = new List<string>();

        public UserPreferencesRecord? Preferences { get; set; }
    }

    private sealed class UserPreferencesRecord
    {
        public bool? ForwardItemAdded { get; set; }

        public bool? ForwardPlayback { get; set; }

        // Legacy fields, retained for backward-compat deserialization
        public bool? ForwardPlaybackStart { get; set; }

        public bool? ForwardPlaybackStop { get; set; }
    }
}
