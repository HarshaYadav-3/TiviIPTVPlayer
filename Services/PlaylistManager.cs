using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TiviIPTVPlayer.Models;
using Windows.Storage;

namespace TiviIPTVPlayer.Services
{
    /// <summary>Result of a playlist refresh operation.</summary>
    public sealed class RefreshResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;
        public int ChannelCount { get; init; }
        public bool FromCache { get; init; }
    }

    /// <summary>
    /// Owns the playlist source list, the parsed channels of each source and the
    /// per-source EPG data. Sources are persisted to LocalFolder/playlists/index.json,
    /// downloaded M3U content is cached per source so the app starts instantly.
    /// </summary>
    public sealed class PlaylistManager
    {
        private static readonly Lazy<PlaylistManager> _instance = new(() => new PlaylistManager());
        public static PlaylistManager Instance => _instance.Value;

        private readonly List<PlaylistSource> _sources = new();
        private readonly Dictionary<string, List<IptvChannel>> _channels = new();
        private readonly Dictionary<string, EpgData> _epgData = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private string _rootDir = string.Empty;

        /// <summary>All playlist sources.</summary>
        public IReadOnlyList<PlaylistSource> Sources => _sources;

        public PlaylistSource? ActiveSource { get; private set; }

        /// <summary>Channels of the active playlist (empty until first load).</summary>
        public IReadOnlyList<IptvChannel> Channels =>
            ActiveSource != null && _channels.TryGetValue(ActiveSource.Id, out var list)
                ? list
                : Array.Empty<IptvChannel>();

        /// <summary>EPG data of the active playlist, when one was loaded.</summary>
        public EpgData? ActiveEpg =>
            ActiveSource != null && _epgData.TryGetValue(ActiveSource.Id, out var epg)
                ? epg
                : null;

        public bool IsBusy { get; private set; }

        /// <summary>Raised after the source list changes (add/remove/rename/refresh metadata).</summary>
        public event Action? SourcesChanged;

        /// <summary>Raised after the active playlist's channel list was replaced.</summary>
        public event Action? ActiveChannelsChanged;

        /// <summary>Raised when a refresh starts or finishes for any source.</summary>
        public event Action<PlaylistSource, bool /*started*/>? RefreshStateChanged;

        /// <summary>Raised after EPG data for a source was (re)loaded or cleared.</summary>
        public event Action<string>? EpgChanged;

        private string RootDir => AppPaths.PlaylistsFolder;

        private string IndexPath => Path.Combine(RootDir, "index.json");

        private string CachePath(PlaylistSource s) => Path.Combine(RootDir, s.Id + ".m3u");

        // ---------------------------------------------------------------- init

        /// <summary>Loads the persisted sources and the active playlist from cache.</summary>
        public async Task InitializeAsync()
        {
            try
            {
                if (File.Exists(IndexPath))
                {
                    var json = await File.ReadAllTextAsync(IndexPath);
                    var loaded = JsonConvert.DeserializeObject<List<PlaylistSource>>(json);
                    if (loaded != null)
                    {
                        _sources.Clear();
                        _sources.AddRange(loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlaylistManager.InitializeAsync(index)");
            }

            var activeId = SettingsService.ActivePlaylistId;
            var active = _sources.FirstOrDefault(s => s.Id == activeId) ?? _sources.FirstOrDefault();

            // Restore channels for every cached source so switching is instant.
            foreach (var source in _sources)
            {
                LoadFromCache(source);
            }

            if (active != null)
            {
                SetActiveInternal(active, notify: false);
            }
            else
            {
                ActiveSource = null;
            }

            AppLogger.LogAction("PlaylistManagerInitialized", new Dictionary<string, object>
            {
                { "sources", _sources.Count },
                { "active", active?.Name ?? "(none)" },
                { "channels", Channels.Count },
            });

            await SaveIndexAsync();
        }

        private void LoadFromCache(PlaylistSource source)
        {
            try
            {
                var path = CachePath(source);
                if (!File.Exists(path))
                {
                    return;
                }

                var parsed = M3UParser.Parse(File.ReadAllText(path));
                _channels[source.Id] = parsed.Channels;
                if (parsed.Channels.Count > 0 && source.ChannelCount == 0)
                {
                    source.ChannelCount = parsed.Channels.Count;
                }
                if (string.IsNullOrEmpty(source.EpgUrl) && !string.IsNullOrEmpty(parsed.EpgUrl))
                {
                    source.EpgUrl = parsed.EpgUrl;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"PlaylistManager.LoadFromCache({source.Name})");
            }
        }

        private void SetActiveInternal(PlaylistSource source, bool notify = true)
        {
            ActiveSource = source;
            SettingsService.ActivePlaylistId = source.Id;
            if (notify)
            {
                ActiveChannelsChanged?.Invoke();
                SourcesChanged?.Invoke();
            }
        }

        private async Task SaveIndexAsync()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_sources, Formatting.Indented);
                await File.WriteAllTextAsync(IndexPath, json);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "PlaylistManager.SaveIndexAsync");
            }
        }

        // ------------------------------------------------------------- actions

        public PlaylistSource? GetSource(string id) => _sources.FirstOrDefault(s => s.Id == id);

        /// <summary>Switches the active playlist. Loads from cache; refresh kicks off separately.</summary>
        public void SetActive(string id)
        {
            var source = GetSource(id);
            if (source == null || ActiveSource?.Id == id)
            {
                return;
            }

            SetActiveInternal(source);
            AppLogger.LogAction("PlaylistActivated", new Dictionary<string, object> { { "name", source.Name } });
        }

        /// <summary>Downloads (or re-downloads) a playlist, parses and caches it.</summary>
        public async Task<RefreshResult> RefreshAsync(PlaylistSource source, bool forceReloadEpg = false)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            await _refreshLock.WaitAsync();
            try
            {
                IsBusy = true;
                source.LastError = string.Empty;
                RefreshStateChanged?.Invoke(source, true);

                try
                {
                    string content;
                    if (source.Kind == PlaylistKind.File)
                    {
                        // File sources: re-read the original if reachable, else the cached copy.
                        if (File.Exists(source.Url))
                        {
                            content = await File.ReadAllTextAsync(source.Url);
                        }
                        else if (File.Exists(CachePath(source)))
                        {
                            content = await File.ReadAllTextAsync(CachePath(source));
                        }
                        else
                        {
                            throw new FileNotFoundException(
                                $"The imported file is no longer available: {source.Url}");
                        }
                    }
                    else
                    {
                        using var client = HttpUtil.CreateClient();
                        content = await client.GetStringAsync(source.Url);
                    }

                    var parsed = M3UParser.Parse(content);
                    if (parsed.Channels.Count == 0)
                    {
                        throw new InvalidDataException(
                            "The playlist was downloaded but contained no channels. " +
                            "Verify it is a valid M3U/M3U8 playlist.");
                    }

                    _channels[source.Id] = parsed.Channels;
                    source.ChannelCount = parsed.Channels.Count;
                    source.LastRefreshedUtc = DateTime.UtcNow;

                    try
                    {
                        await File.WriteAllTextAsync(CachePath(source), content);
                    }
                    catch (Exception cacheEx)
                    {
                        AppLogger.LogException(cacheEx, $"PlaylistManager.CacheWrite({source.Name})");
                    }

                    // Adopt the EPG url announced by the playlist header if the
                    // user has not set one explicitly.
                    if (string.IsNullOrEmpty(source.EpgUrl) && !string.IsNullOrEmpty(parsed.EpgUrl))
                    {
                        source.EpgUrl = parsed.EpgUrl;
                    }

                    await SaveIndexAsync();

                    AppLogger.LogAction("PlaylistRefreshed", new Dictionary<string, object>
                    {
                        { "name", source.Name },
                        { "channels", parsed.Channels.Count },
                        { "epgUrl", parsed.EpgUrl },
                    });

                    if (source.Id == ActiveSource?.Id)
                    {
                        ActiveChannelsChanged?.Invoke();
                    }
                    SourcesChanged?.Invoke();

                    if (SettingsService.LoadEpg && !string.IsNullOrEmpty(source.EpgUrl))
                    {
                        await LoadEpgAsync(source, forceReloadEpg);
                    }

                    return new RefreshResult
                    {
                        Success = true,
                        ChannelCount = parsed.Channels.Count,
                    };
                }
                catch (Exception ex)
                {
                    source.LastError = ex.Message;
                    AppLogger.LogException(ex, $"PlaylistManager.RefreshAsync({source.Name})");

                    // Keep whatever cache exists so the UI stays usable offline.
                    if (_channels.TryGetValue(source.Id, out var cached) && cached.Count > 0)
                    {
                        if (source.Id == ActiveSource?.Id)
                        {
                            ActiveChannelsChanged?.Invoke();
                        }
                    }

                    return new RefreshResult
                    {
                        Success = false,
                        Error = ex.Message,
                        ChannelCount = _channels.TryGetValue(source.Id, out var c) ? c.Count : 0,
                    };
                }
                finally
                {
                    RefreshStateChanged?.Invoke(source, false);
                    IsBusy = false;
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>Downloads a playlist from a URL and registers it as a new source.</summary>
        public async Task<(PlaylistSource? Source, string Error)> AddUrlPlaylistAsync(string url, string? name = null)
        {
            url = url.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return (null, "Enter a valid http:// or https:// playlist URL.");
            }

            var source = new PlaylistSource
            {
                Name = string.IsNullOrWhiteSpace(name) ? GuessName(uri) : name!.Trim(),
                Kind = PlaylistKind.Url,
                Url = url,
            };

            var result = await RefreshAsync(source);
            if (!result.Success)
            {
                return (null, result.Error);
            }

            _sources.Add(source);
            await SaveIndexAsync();
            SourcesChanged?.Invoke();
            AppLogger.LogAction("PlaylistAddedUrl", new Dictionary<string, object>
            {
                { "name", source.Name },
                { "channels", source.ChannelCount },
            });
            return (source, string.Empty);
        }

        /// <summary>Imports a local .m3u/.m3u8 file and caches its content.</summary>
        public async Task<(PlaylistSource? Source, string Error)> ImportFilePlaylistAsync(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return (null, "File not found.");
                }

                var content = await File.ReadAllTextAsync(path);
                var parsed = M3UParser.Parse(content);
                if (parsed.Channels.Count == 0)
                {
                    return (null,
                        "This file contains no channels. Verify it is a valid M3U/M3U8 playlist.");
                }

                var source = new PlaylistSource
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Kind = PlaylistKind.File,
                    Url = path,
                    ChannelCount = parsed.Channels.Count,
                    LastRefreshedUtc = DateTime.UtcNow,
                    EpgUrl = parsed.EpgUrl,
                };

                _channels[source.Id] = parsed.Channels;
                try
                {
                    await File.WriteAllTextAsync(CachePath(source), content);
                }
                catch (Exception cacheEx)
                {
                    AppLogger.LogException(cacheEx, "ImportFilePlaylist(cache)");
                }

                _sources.Add(source);
                await SaveIndexAsync();
                SourcesChanged?.Invoke();
                ActiveChannelsChangedIfActive(source);

                AppLogger.LogAction("PlaylistImportedFile", new Dictionary<string, object>
                {
                    { "name", source.Name },
                    { "channels", source.ChannelCount },
                });
                return (source, string.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ImportFilePlaylistAsync");
                return (null, ex.Message);
            }
        }

        /// <summary>Removes a non-default playlist source and its cache.</summary>
        public async Task<string> RemoveSourceAsync(PlaylistSource source)
        {
            try
            {
                _sources.Remove(source);
                _channels.Remove(source.Id);
                _epgData.Remove(source.Id);
                var cache = CachePath(source);
                if (File.Exists(cache))
                {
                    File.Delete(cache);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RemoveSourceAsync");
            }

            if (ActiveSource?.Id == source.Id)
            {
                var fallback = _sources.FirstOrDefault();
                if (fallback != null)
                {
                    SetActiveInternal(fallback);
                }
                else
                {
                    ActiveSource = null;
                    SettingsService.ActivePlaylistId = string.Empty;
                    ActiveChannelsChanged?.Invoke();
                    SourcesChanged?.Invoke();
                }
            }

            await SaveIndexAsync();
            SourcesChanged?.Invoke();
            AppLogger.LogAction("PlaylistRemoved", new Dictionary<string, object> { { "name", source.Name } });
            return string.Empty;
        }

        public async Task RenameSourceAsync(PlaylistSource source, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            source.Name = newName.Trim();
            await SaveIndexAsync();
            SourcesChanged?.Invoke();
        }

        /// <summary>Sets (or clears with null/empty) the EPG URL and reloads the guide.</summary>
        public async Task SetEpgUrlAsync(PlaylistSource source, string? epgUrl)
        {
            source.EpgUrl = epgUrl?.Trim() ?? string.Empty;
            await SaveIndexAsync();
            SourcesChanged?.Invoke();

            if (!string.IsNullOrEmpty(source.EpgUrl) && SettingsService.LoadEpg)
            {
                await LoadEpgAsync(source, forceReload: true);
            }
            else
            {
                _epgData.Remove(source.Id);
                EpgChanged?.Invoke(source.Id);
            }
        }

        /// <summary>Downloads and parses the XMLTV guide for a source.</summary>
        public async Task<EpgData?> LoadEpgAsync(PlaylistSource source, bool forceReload = false)
        {
            if (string.IsNullOrEmpty(source.EpgUrl))
            {
                return null;
            }

            if (!forceReload && _epgData.TryGetValue(source.Id, out var existing)
                && existing.ChannelCount > 0
                && existing.LoadedAtUtc > DateTime.UtcNow.AddHours(-6))
            {
                return existing;
            }

            try
            {
                var isFile = File.Exists(source.EpgUrl);
                var data = isFile
                    ? await EpgService.LoadFromFileAsync(source.EpgUrl)
                    : await EpgService.LoadFromUrlAsync(source.EpgUrl);

                _epgData[source.Id] = data;
                EpgChanged?.Invoke(source.Id);
                AppLogger.LogAction("EpgLoaded", new Dictionary<string, object>
                {
                    { "source", source.Name },
                    { "channels", data.ChannelCount },
                    { "programmes", data.ProgrammeCount },
                });
                return data;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"LoadEpgAsync({source.Name})");
                return null;
            }
        }

        private void ActiveChannelsChangedIfActive(PlaylistSource source)
        {
            if (ActiveSource?.Id == source.Id)
            {
                ActiveChannelsChanged?.Invoke();
            }
        }

        private static string GuessName(Uri uri)
        {
            // Prefer a meaningful path segment (…/countries/us.m3u -> "US"),
            // skipping generic file names, else fall back to the host.
            var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "index", "playlist", "iptv", "list", "get", "play", "stream", "channels",
            };

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var seg = segments[i];
                var withoutExt = seg.IndexOf('.') > 0 ? seg.Substring(0, seg.LastIndexOf('.')) : seg;
                if (!generic.Contains(withoutExt) && withoutExt.Length >= 2)
                {
                    return withoutExt.ToUpperInvariant();
                }
            }

            var host = uri.Host.Replace("www.", string.Empty);
            return host;
        }
    }
}
