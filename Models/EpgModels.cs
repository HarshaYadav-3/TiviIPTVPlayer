using System;
using System.Collections.Generic;

namespace TiviIPTVPlayer.Models
{
    /// <summary>One XMLTV programme entry.</summary>
    public sealed class EpgProgramme
    {
        public string ChannelId { get; set; } = string.Empty;
        public DateTime StartUtc { get; set; }
        public DateTime StopUtc { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        public bool IsCurrentAt(DateTime utc) => utc >= StartUtc && utc < StopUtc;

        public string TimeRangeLocal => $"{StartUtc.ToLocalTime():HH:mm} – {StopUtc.ToLocalTime():HH:mm}";
    }

    /// <summary>Parsed EPG data keyed by XMLTV channel id.</summary>
    public sealed class EpgData
    {
        public DateTime LoadedAtUtc { get; } = DateTime.UtcNow;

        public string SourceUrl { get; set; } = string.Empty;

        public Dictionary<string, List<EpgProgramme>> Channels { get; } =
            new Dictionary<string, List<EpgProgramme>>(StringComparer.OrdinalIgnoreCase);

        public int ProgrammeCount { get; set; }

        public int ChannelCount => Channels.Count;

        /// <summary>Returns the current and next programme for a channel tvg-id, if known.</summary>
        public (EpgProgramme? Current, EpgProgramme? Next) GetCurrentAndNext(string tvgId)
        {
            if (string.IsNullOrWhiteSpace(tvgId))
            {
                return (null, null);
            }

            var now = DateTime.UtcNow;
            List<EpgProgramme>? programmes = Resolve(tvgId);
            if (programmes == null)
            {
                return (null, null);
            }

            EpgProgramme? current = null;
            EpgProgramme? next = null;
            foreach (var p in programmes)
            {
                if (p.IsCurrentAt(now))
                {
                    current = p;
                }
                else if (p.StartUtc > now && next == null)
                {
                    next = p;
                }
            }

            return (current, next);
        }

        /// <summary>All programmes (sorted) for a channel id, when available.</summary>
        public IReadOnlyList<EpgProgramme>? GetSchedule(string tvgId)
        {
            var programmes = Resolve(tvgId);
            return programmes;
        }

        private List<EpgProgramme>? Resolve(string tvgId)
        {
            // Match in decreasing order of specificity: exact id, id without the
            // "@HD"/"@SD" quality suffix used by common playlists, then tvg-name.
            if (Channels.TryGetValue(tvgId, out var exact))
            {
                return exact;
            }

            var cut = tvgId.IndexOf('@');
            if (cut > 0)
            {
                var baseId = tvgId.Substring(0, cut);
                if (Channels.TryGetValue(baseId, out var byBase))
                {
                    return byBase;
                }
            }

            return null;
        }
    }
}
