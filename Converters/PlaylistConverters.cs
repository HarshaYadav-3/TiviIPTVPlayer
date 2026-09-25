using System;
using Microsoft.UI.Xaml;
using TiviIPTVPlayer.Services;

namespace TiviIPTVPlayer.Converters
{
    /// <summary>Converters for playlist source rows (read PlaylistManager active state).</summary>
    public static class PlaylistConverters
    {
        private static bool IsActive(string id) =>
            PlaylistManager.Instance.ActiveSource?.Id == id;

        public static string ActiveGlyph(string id) => "";

        public static Visibility ActiveVisibility(string id)
            => IsActive(id) ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility DefaultVisibility(bool isDefault)
            => isDefault ? Visibility.Visible : Visibility.Collapsed;

        public static string ActivateText(string id) => IsActive(id) ? "Active" : "Activate";

        public static bool ActivateEnabled(string id) => !IsActive(id);

        public static bool RemoveEnabled(bool isDefault) => !isDefault;

        public static Visibility ErrorVisibility(string value)
            => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

        public static string SourceDetail(string kindLabel, int channelCount, DateTime? lastRefreshedUtc, string epgUrl)
        {
            var updated = lastRefreshedUtc.HasValue
                ? lastRefreshedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "never refreshed";
            var epg = string.IsNullOrWhiteSpace(epgUrl)
                ? "no EPG guide"
                : $"EPG: {epgUrl}";
            return $"{kindLabel} · {ChannelConverters.PluralChannels(channelCount)} · updated {updated} · {epg}";
        }
    }
}
