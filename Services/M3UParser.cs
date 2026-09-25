using System;
using System.Collections.Generic;
using System.Text;
using TiviIPTVPlayer.Models;

namespace TiviIPTVPlayer.Services
{
    /// <summary>Result of parsing an M3U playlist.</summary>
    public sealed class M3uParseResult
    {
        /// <summary>EPG (XMLTV) URL announced in the #EXTM3U header, if any.</summary>
        public string EpgUrl { get; set; } = string.Empty;

        public List<IptvChannel> Channels { get; } = new List<IptvChannel>();
    }

    /// <summary>
    /// Extended M3U / M3U8 playlist parser used by IPTV playlists.
    /// Understands #EXTM3U header attributes (x-tvg-url / url-tvg),
    /// #EXTINF attributes (tvg-id, tvg-name, tvg-logo, group-title,
    /// http-user-agent, http-referrer), #EXTVLCOPT directives and #EXTGRP.
    /// </summary>
    public static class M3UParser
    {
        public static M3uParseResult Parse(string content)
        {
            var result = new M3uParseResult();
            if (string.IsNullOrWhiteSpace(content))
            {
                return result;
            }

            IptvChannel? pending = null;
            var pendingGroup = string.Empty;

            using var reader = new System.IO.StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                {
                    var attrs = ParseAttributes(trimmed.Substring(7));
                    result.EpgUrl = FirstValue(attrs, "x-tvg-url", "url-tvg");
                    continue;
                }

                if (trimmed.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    pending = ParseExtInf(trimmed);
                    if (pending != null && !string.IsNullOrEmpty(pendingGroup))
                    {
                        if (string.IsNullOrEmpty(pending.Group))
                        {
                            pending.Group = pendingGroup;
                        }
                    }
                    continue;
                }

                if (trimmed.StartsWith("#EXTVLCOPT:", StringComparison.OrdinalIgnoreCase))
                {
                    var opt = trimmed.Substring("#EXTVLCOPT:".Length);
                    if (pending != null)
                    {
                        if (opt.StartsWith("http-user-agent=", StringComparison.OrdinalIgnoreCase))
                        {
                            pending.UserAgent = opt.Substring("http-user-agent=".Length).Trim();
                        }
                        else if (opt.StartsWith("http-referrer=", StringComparison.OrdinalIgnoreCase)
                                 || opt.StartsWith("http-referer=", StringComparison.OrdinalIgnoreCase))
                        {
                            pending.Referrer = opt.Substring(opt.IndexOf('=') + 1).Trim();
                        }
                    }
                    continue;
                }

                if (trimmed.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingGroup = trimmed.Substring("#EXTGRP:".Length).Trim();
                    if (pending != null && string.IsNullOrEmpty(pending.Group))
                    {
                        pending.Group = pendingGroup;
                    }
                    continue;
                }

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    // #KODIPROP and other vendor directives are ignored.
                    continue;
                }

                // Non-comment line: the stream URL for the pending #EXTINF entry.
                if (pending != null)
                {
                    pending.Url = trimmed;
                    if (!string.IsNullOrWhiteSpace(pending.Name))
                    {
                        result.Channels.Add(pending);
                    }
                    pending = null;
                }
            }

            return result;
        }

        private static IptvChannel? ParseExtInf(string line)
        {
            // Format: #EXTINF:<duration>[ <attrs>],<display name>
            // The display name may itself contain commas, so the separator is the
            // first comma that is not inside a quoted attribute value.
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                return null;
            }

            var body = line.Substring(colon + 1);
            var comma = FindUnquotedComma(body);
            string info;
            string title;
            if (comma < 0)
            {
                info = body;
                title = string.Empty;
            }
            else
            {
                info = body.Substring(0, comma);
                title = body.Substring(comma + 1).Trim();
            }

            var attrs = ParseAttributes(info);
            return new IptvChannel
            {
                Name = title,
                TvgId = GetAttr(attrs, "tvg-id"),
                TvgName = GetAttr(attrs, "tvg-name"),
                TvgLogo = GetAttr(attrs, "tvg-logo"),
                Group = GetAttr(attrs, "group-title"),
                UserAgent = GetAttr(attrs, "http-user-agent"),
                Referrer = GetAttr(attrs, "http-referrer"),
            };
        }

        private static int FindUnquotedComma(string s)
        {
            bool inQuotes = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Parse key="value" and key=value pairs from a directive body.</summary>
        private static Dictionary<string, string> ParseAttributes(string s)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(s))
            {
                return map;
            }

            int i = 0;
            var key = new StringBuilder();
            while (i < s.Length)
            {
                // Skip leading separators.
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == ','))
                {
                    i++;
                }
                if (i >= s.Length)
                {
                    break;
                }

                key.Clear();
                while (i < s.Length && s[i] != '=' && s[i] != ' ' && s[i] != ',')
                {
                    key.Append(s[i]);
                    i++;
                }

                while (i < s.Length && s[i] == ' ')
                {
                    i++;
                }

                if (i < s.Length && s[i] == '=')
                {
                    i++; // skip '='
                    while (i < s.Length && s[i] == ' ')
                    {
                        i++;
                    }

                    var value = new StringBuilder();
                    if (i < s.Length && s[i] == '"')
                    {
                        i++;
                        while (i < s.Length && s[i] != '"')
                        {
                            value.Append(s[i]);
                            i++;
                        }
                        if (i < s.Length)
                        {
                            i++; // closing quote
                        }
                    }
                    else
                    {
                        while (i < s.Length && s[i] != ' ' && s[i] != ',')
                        {
                            value.Append(s[i]);
                            i++;
                        }
                    }

                    if (key.Length > 0)
                    {
                        map[key.ToString()] = value.ToString();
                    }
                }
                else if (key.Length > 0)
                {
                    // Bare token (e.g. the "-1" duration) — not an attribute.
                }
            }

            return map;
        }

        private static string GetAttr(Dictionary<string, string> attrs, string key)
            => attrs.TryGetValue(key, out var v) ? v : string.Empty;

        private static string FirstValue(Dictionary<string, string> attrs, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (attrs.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            return string.Empty;
        }
    }
}
