using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace TiviIPTVPlayer.Services
{
    /// <summary>Typed access to persisted app settings (saved to %LOCALAPPDATA%\TiviIPTVPlayer\settings.json).</summary>
    public static class SettingsService
    {
        private static readonly object _syncRoot = new();
        private static Dictionary<string, object> _values = new();
        private static bool _loaded = false;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_syncRoot)
            {
                if (_loaded) return;
                try
                {
                    var file = AppPaths.SettingsFile;
                    if (File.Exists(file))
                    {
                        var json = File.ReadAllText(file);
                        var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (parsed != null)
                        {
                            _values = parsed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "SettingsService.EnsureLoaded");
                }
                _loaded = true;
            }
        }

        private static void Save()
        {
            lock (_syncRoot)
            {
                try
                {
                    var file = AppPaths.SettingsFile;
                    var json = JsonConvert.SerializeObject(_values, Formatting.Indented);
                    File.WriteAllText(file, json);
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "SettingsService.Save");
                }
            }
        }

        public static bool GetBool(string key, bool fallback)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_values.TryGetValue(key, out var val))
                {
                    if (val is bool b) return b;
                    if (bool.TryParse(val?.ToString(), out var parsed)) return parsed;
                }
                return fallback;
            }
        }

        public static void SetBool(string key, bool value)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                _values[key] = value;
                Save();
            }
        }

        public static double GetDouble(string key, double fallback)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_values.TryGetValue(key, out var val))
                {
                    if (val is double d) return d;
                    if (val is long l) return l;
                    if (double.TryParse(val?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
                }
                return fallback;
            }
        }

        public static void SetDouble(string key, double value)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                _values[key] = value;
                Save();
            }
        }

        public static string GetString(string key, string fallback = "")
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                if (_values.TryGetValue(key, out var val) && val != null)
                {
                    return val.ToString() ?? fallback;
                }
                return fallback;
            }
        }

        public static void SetString(string key, string? value)
        {
            EnsureLoaded();
            lock (_syncRoot)
            {
                _values[key] = value ?? string.Empty;
                Save();
            }
        }

        // ---- playlists

        public static string ActivePlaylistId
        {
            get => GetString("ActivePlaylistId");
            set => SetString("ActivePlaylistId", value);
        }

        /// <summary>Refresh the active playlist from its source on launch.</summary>
        public static bool AutoRefreshOnLaunch
        {
            get => GetBool("AutoRefreshOnLaunch", true);
            set => SetBool("AutoRefreshOnLaunch", value);
        }

        /// <summary>Download + parse the XMLTV guide when the playlist provides one.</summary>
        public static bool LoadEpg
        {
            get => GetBool("LoadEpg", true);
            set => SetBool("LoadEpg", value);
        }

        // ---- playback

        /// <summary>Resume the last watched channel on launch.</summary>
        public static bool ResumeLastChannel
        {
            get => GetBool("ResumeLastChannel", false);
            set => SetBool("ResumeLastChannel", value);
        }

        public static string LastChannelId
        {
            get => GetString("LastChannelId");
            set => SetString("LastChannelId", value);
        }

        public static double Volume
        {
            get => GetDouble("Volume", 0.8);
            set => SetDouble("Volume", Math.Clamp(value, 0, 1));
        }

        public static bool Muted
        {
            get => GetBool("Muted", false);
            set => SetBool("Muted", value);
        }

        // ---- misc

        public static bool IsRunAtStartup
        {
            get => GetBool("IsRunAtStartup", false);
            set => SetBool("IsRunAtStartup", value);
        }

        public static long CacheSizeBytes()
        {
            try
            {
                var dir = AppPaths.PlaylistsFolder;
                if (!Directory.Exists(dir))
                {
                    return 0;
                }

                long total = 0;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        total += info.Length;
                    }
                    catch
                    {
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }
    }
}
