using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TiviIPTVPlayer.Models;

namespace TiviIPTVPlayer.Services
{
    /// <summary>Persisted set of favorite channel ids, shared across playlists.</summary>
    public sealed class FavoritesService
    {
        private static readonly Lazy<FavoritesService> _instance = new(() => new FavoritesService());
        public static FavoritesService Instance => _instance.Value;

        private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);

        public event Action? FavoritesChanged;

        private FavoritesService()
        {
            try
            {
                var file = AppPaths.FavoritesFile;
                if (File.Exists(file))
                {
                    var raw = File.ReadAllText(file);
                    var loaded = JsonConvert.DeserializeObject<List<string>>(raw);
                    if (loaded != null)
                    {
                        foreach (var id in loaded)
                        {
                            if (!string.IsNullOrEmpty(id))
                            {
                                _ids.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesService.Load");
            }
        }

        public int Count => _ids.Count;

        public bool IsFavorite(IptvChannel channel) => _ids.Contains(channel.Id);

        /// <summary>Toggles a channel favorite and returns the new state.</summary>
        public bool Toggle(IptvChannel channel)
        {
            bool nowFavorite;
            if (_ids.Contains(channel.Id))
            {
                _ids.Remove(channel.Id);
                nowFavorite = false;
            }
            else
            {
                _ids.Add(channel.Id);
                nowFavorite = true;
            }

            channel.IsFavorite = nowFavorite;
            Save();
            FavoritesChanged?.Invoke();
            AppLogger.LogAction("FavoriteToggled", new Dictionary<string, object>
            {
                { "channel", channel.Name },
                { "favorite", nowFavorite },
            });
            return nowFavorite;
        }

        public void ClearAll()
        {
            _ids.Clear();
            Save();
            FavoritesChanged?.Invoke();
        }

        private void Save()
        {
            try
            {
                var file = AppPaths.FavoritesFile;
                File.WriteAllText(file, JsonConvert.SerializeObject(_ids, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesService.Save");
            }
        }
    }
}
