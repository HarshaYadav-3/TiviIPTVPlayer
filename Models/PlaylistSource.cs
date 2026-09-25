using System;

namespace TiviIPTVPlayer.Models
{
    public enum PlaylistKind
    {
        /// <summary>Streamed from an M3U/M3U8 URL.</summary>
        Url,

        /// <summary>Imported from a local .m3u/.m3u8 file (cached copy kept in app data).</summary>
        File,
    }

    /// <summary>A playlist source the user can activate, refresh or remove.</summary>
    public sealed class PlaylistSource
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = string.Empty;

        public PlaylistKind Kind { get; set; } = PlaylistKind.Url;

        /// <summary>Remote playlist URL (Url kind) or original file path (File kind).</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional XMLTV EPG URL supplied by the playlist header or set by the user.</summary>
        public string EpgUrl { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public int ChannelCount { get; set; }

        public DateTime? LastRefreshedUtc { get; set; }

        public string LastError { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonIgnore]
        public string KindLabel => Kind == PlaylistKind.Url ? "URL" : "File";

        [Newtonsoft.Json.JsonIgnore]
        public string Detail
        {
            get
            {
                var refreshed = LastRefreshedUtc.HasValue
                    ? LastRefreshedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : "never";
                return $"{ChannelCount} channels · updated {refreshed}";
            }
        }
    }
}
