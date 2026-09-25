using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using TiviIPTVPlayer.Models;

namespace TiviIPTVPlayer.Services
{
    /// <summary>
    /// Downloads and parses XMLTV (EPG) documents. Programmes outside the
    /// [now - 6h, now + 48h] window are skipped to bound memory on large guides.
    /// </summary>
    public static class EpgService
    {
        /// <summary>Parses an XMLTV document from disk (.gz guides are decompressed).</summary>
        public static Task<EpgData> LoadFromFileAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                Stream stream = File.OpenRead(path);
                if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || PeekIsGzip(stream))
                {
                    stream = new System.IO.Compression.GZipStream(
                        stream, System.IO.Compression.CompressionMode.Decompress);
                }

                await using (stream.ConfigureAwait(false))
                {
                    return await ParseAsync(stream, path, ct);
                }
            }, ct);
        }

        /// <summary>Downloads an XMLTV document over HTTP(S) and parses it.</summary>
        public static Task<EpgData> LoadFromUrlAsync(string url, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                using var client = HttpUtil.CreateClient();
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(ct);

                // Many public guides ship as .gz FILES (not content-encoding gzip).
                if (url.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || PeekIsGzip(stream))
                {
                    stream = new System.IO.Compression.GZipStream(
                        stream, System.IO.Compression.CompressionMode.Decompress);
                }

                await using (stream.ConfigureAwait(false))
                {
                    return await ParseAsync(stream, url, ct);
                }
            }, ct);
        }

        /// <summary>Reads the two gzip magic bytes (consuming nothing else).</summary>
        private static bool PeekIsGzip(Stream stream)
        {
            try
            {
                if (!stream.CanSeek)
                {
                    return false;
                }

                long pos = stream.Position;
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                stream.Position = pos;
                return first == 0x1F && second == 0x8B;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<EpgData> ParseAsync(Stream stream, string sourceUrl, CancellationToken ct)
        {
            var data = new EpgData { SourceUrl = sourceUrl };
            var now = DateTime.UtcNow;
            var minStart = now.AddHours(-6);
            var maxStart = now.AddHours(48);

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                Async = true,
            };

            using var reader = XmlReader.Create(stream, settings);
            while (await reader.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.Name, "programme", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var channel = reader.GetAttribute("channel") ?? string.Empty;
                var start = ParseXmlTvTime(reader.GetAttribute("start"));
                var stop = ParseXmlTvTime(reader.GetAttribute("stop"));
                var title = string.Empty;
                var desc = string.Empty;
                var category = string.Empty;

                try
                {
                    using var sub = reader.ReadSubtree();
                    while (await sub.ReadAsync())
                    {
                        if (sub.NodeType != XmlNodeType.Element)
                        {
                            continue;
                        }

                        if (string.Equals(sub.Name, "title", StringComparison.OrdinalIgnoreCase))
                        {
                            title = await sub.ReadElementContentAsStringAsync();
                        }
                        else if (string.Equals(sub.Name, "desc", StringComparison.OrdinalIgnoreCase))
                        {
                            desc = await sub.ReadElementContentAsStringAsync();
                        }
                        else if (string.Equals(sub.Name, "category", StringComparison.OrdinalIgnoreCase))
                        {
                            var value = await sub.ReadElementContentAsStringAsync();
                            if (string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(value))
                            {
                                category = value;
                            }
                        }
                    }
                }
                catch (XmlException)
                {
                    // Malformed programme entry — skip it, keep the rest of the guide.
                    continue;
                }

                AddProgramme(data, channel, start, stop, title, desc, category, minStart, maxStart);
            }

            foreach (var list in data.Channels.Values)
            {
                list.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            }

            return data;
        }

        private static void AddProgramme(
            EpgData data,
            string channelId,
            DateTime start,
            DateTime stop,
            string title,
            string desc,
            string category,
            DateTime minStart,
            DateTime maxStart)
        {
            if (string.IsNullOrWhiteSpace(channelId) || start == DateTime.MinValue)
            {
                return;
            }

            if (start < minStart || start > maxStart)
            {
                return;
            }

            if (stop != DateTime.MinValue && stop <= start)
            {
                return;
            }

            if (!data.Channels.TryGetValue(channelId, out var list))
            {
                list = new System.Collections.Generic.List<EpgProgramme>();
                data.Channels[channelId] = list;
            }

            // Cap entries per channel to bound memory on very large guides.
            if (list.Count >= 120)
            {
                return;
            }

            list.Add(new EpgProgramme
            {
                ChannelId = channelId,
                StartUtc = start,
                StopUtc = stop,
                Title = title.Trim(),
                Description = desc.Trim(),
                Category = category.Trim(),
            });
            data.ProgrammeCount++;
        }

        /// <summary>Parses XMLTV timestamps like "20260924103000 +0000" or "20260924103000".</summary>
        public static DateTime ParseXmlTvTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            var s = value.Trim();
            int year, month, day, hour, minute, second;

            try
            {
                if (s.Length < 14 || !int.TryParse(s.Substring(0, 4), out year))
                {
                    return DateTime.MinValue;
                }

                month = int.Parse(s.Substring(4, 2));
                day = int.Parse(s.Substring(6, 2));
                hour = int.Parse(s.Substring(8, 2));
                minute = int.Parse(s.Substring(10, 2));
                second = int.Parse(s.Substring(12, 2));

                var utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

                // Optional timezone offset, e.g. " +0700" or "-0300".
                var space = s.IndexOf(' ');
                if (space >= 0 && s.Length - space >= 6)
                {
                    var sign = s[space + 1];
                    if ((sign == '+' || sign == '-') &&
                        int.TryParse(s.Substring(space + 2, 2), out var oh) &&
                        int.TryParse(s.Substring(space + 4, 2), out var om))
                    {
                        var offset = new TimeSpan(oh, om, 0);
                        if (sign == '-')
                        {
                            offset = offset.Negate();
                        }
                        utc -= offset;
                    }
                }

                return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }
    }
}
