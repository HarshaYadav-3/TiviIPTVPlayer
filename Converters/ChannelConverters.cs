using System;
using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TiviIPTVPlayer.Models;

namespace TiviIPTVPlayer.Converters
{
    /// <summary>x:Bind function converters shared across pages.</summary>
    public static class ChannelConverters
    {
        // BitmapImage instances are lazy (nothing decodes until drawn), so a
        // bounded url->image cache is cheap and avoids re-creating bitmaps on
        // list recycling.
        private static readonly ConcurrentDictionary<string, BitmapImage> LogoCache = new();

        private const int CacheCap = 2500;

        public static string StarGlyph(bool isFavorite) => isFavorite ? "" : "";

        public static string FavoriteTooltip(bool isFavorite)
            => isFavorite ? "Remove from favorites" : "Add to favorites";

        public static Visibility LogoVisibility(string url)
            => string.IsNullOrWhiteSpace(url) ? Visibility.Collapsed : Visibility.Visible;

        public static ImageSource? Logo(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (LogoCache.TryGetValue(url, out var cached))
            {
                return cached;
            }

            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    return null;
                }

                var image = new BitmapImage(uri)
                {
                    DecodePixelWidth = 72,
                    DecodePixelType = DecodePixelType.Logical,
                };

                if (LogoCache.Count < CacheCap)
                {
                    LogoCache[url] = image;
                }

                return image;
            }
            catch
            {
                return null;
            }
        }

        public static string PluralChannels(int count)
            => count == 1 ? "1 channel" : $"{count:N0} channels";

        public static string GroupOrUnfiled(IptvChannel channel)
            => string.IsNullOrWhiteSpace(channel.Group) ? "Uncategorized" : channel.Group;
    }
}
