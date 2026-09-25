using System;
using System.ComponentModel;

namespace TiviIPTVPlayer.Models
{
    /// <summary>A single IPTV channel parsed from an M3U/M3U8 playlist.</summary>
    public sealed class IptvChannel : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string TvgId { get; set; } = string.Empty;
        public string TvgName { get; set; } = string.Empty;
        public string TvgLogo { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string Referrer { get; set; } = string.Empty;

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
                }
            }
        }

        /// <summary>Stable identity for favorites/last-channel persistence (name + stream URL).</summary>
        public string Id
        {
            get
            {
                var raw = Name + "|" + Url;
#if NET6_0_OR_GREATER
                return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(raw)))[..16];
#else
                using var sha = System.Security.Cryptography.SHA1.Create();
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("X2"));
                return sb.ToString();
#endif
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Name;
    }
}
