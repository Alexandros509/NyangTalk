using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyangTalk.Shared
{
    /// <summary>
    /// Retention of the exchange message files (40-SPEC 1.2): same period as the conversation records, chosen by the user
    /// (0 = keep forever). The app writes the period to exchange\settings.json; the game component trims old lines with it
    /// even when the app is not run for a long time.
    /// </summary>
    public static class ExchangeRetention
    {
        public const string SettingsFile = "settings.json";

        /// <summary>Lines whose time_utc is older than the period are dropped; unreadable lines are kept (never guess).</summary>
        public static List<string> Keep(IEnumerable<string> lines, DateTime nowUtc, int days, out int removed)
        {
            removed = 0;
            var keep = new List<string>();
            foreach (var l in lines)
            {
                if (l.Length == 0) continue;
                if (days > 0)
                {
                    object o;
                    DateTime t;
                    if (Json.TryParse(l, out o) && Exchange.TryIso(Json.Str(Json.Obj(o), "time_utc"), out t) && nowUtc - t > TimeSpan.FromDays(days)) { removed++; continue; }
                }
                keep.Add(l);
            }
            return keep;
        }

        /// <summary>Rewrites one lobby file without its expired lines. Returns removed line count. Only call when no one appends.</summary>
        public static int TrimFile(string path, DateTime nowUtc, int days)
        {
            if (days <= 0 || !File.Exists(path)) return 0;
            var text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            int lastNl = text.LastIndexOf('\n');
            var complete = lastNl < 0 ? "" : text.Substring(0, lastNl + 1);
            var partial = lastNl < 0 ? text : text.Substring(lastNl + 1); // a line still being written stays as is
            int removed;
            var keep = Keep(complete.Split('\n'), nowUtc, days, out removed);
            if (removed == 0) return 0;
            var sb = new StringBuilder();
            foreach (var k in keep) sb.Append(k).Append('\n');
            sb.Append(partial);
            var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(); }
            return removed;
        }

        public static int ReadDays(string exchangeDir, int fallback)
        {
            var p = Path.Combine(exchangeDir, SettingsFile);
            if (!File.Exists(p)) return fallback;
            object o;
            if (!Json.TryParse(Encoding.UTF8.GetString(File.ReadAllBytes(p)), out o)) return fallback;
            return (int)Json.Long(Json.Obj(o), "retention_days", fallback);
        }
    }
}
