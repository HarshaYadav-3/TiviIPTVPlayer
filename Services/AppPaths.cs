using System;
using System.IO;

namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Local application paths rooted at %LOCALAPPDATA%\TiviIPTVPlayer (pure unpackaged-safe).
    /// </summary>
    public static class AppPaths
    {
        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiviIPTVPlayer");

        public static string PlaylistsFolder => EnsureFolder("playlists");
        public static string LogsFolder => EnsureFolder("logs");

        public static string SettingsFile => FilePath("settings.json");
        public static string FavoritesFile => FilePath("favorites.json");

        public static string SnapshotsFolder
        {
            get
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var folder = Path.Combine(pictures, "TiviIPTVPlayer");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public static string FilePath(string fileName)
        {
            Directory.CreateDirectory(Root);
            return Path.Combine(Root, fileName);
        }

        private static string EnsureFolder(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
