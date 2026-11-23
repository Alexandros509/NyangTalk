using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NyangTalk.Domain.People;

namespace NyangTalk.Domain.Conversation
{
    /// <summary>
    /// How many messages are shown (50-SPEC 4장). Meows are not counted against the limit (3장),
    /// counts are made before grouping.
    /// </summary>
    public sealed class HistoryWindow
    {
        public const int Initial = 300;
        public const int Step = 500;
        public const int Max = 3000;

        private int _limit = Initial;
        public int Limit { get { return _limit; } }

        public void Expand() { _limit = Math.Min(Max, _limit + Step); }
        public void Collapse() { _limit = Initial; }

        /// <summary>Counted messages = non-meow messages.</summary>
        public static bool Counts(ChatMessage m) { return !MeowCatalog.IsMeow(m); }

        /// <summary>"이전 대화 500개 더 보기" is offered only if older counted messages exist beyond the limit and the limit is below max.</summary>
        public bool CanExpand(int countedAvailable) { return _limit < Max && countedAvailable > _limit; }
        public bool CanCollapse { get { return _limit > Initial; } }

        /// <summary>
        /// Returns the tail slice: the last <see cref="Limit"/> counted messages plus every meow that falls after the
        /// oldest selected counted message. The record file itself is never changed.
        /// </summary>
        public List<ChatMessage> Select(IList<ChatMessage> all)
        {
            int counted = 0;
            int start = 0;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (!Counts(all[i])) continue;
                counted++;
                if (counted == _limit) { start = i; break; }
            }
            if (counted < _limit) start = 0;
            var r = new List<ChatMessage>(all.Count - start);
            for (int i = start; i < all.Count; i++) r.Add(all[i]);
            return r;
        }
    }

    public static class TextNormalizer
    {
        /// <summary>Fold full-width ASCII forms (U+FF01–FF5E) and ideographic space to their half-width forms.</summary>
        public static string Normalize(string s, bool ignoreCase, bool ignoreWidth)
        {
            if (s == null) return "";
            if (!ignoreCase && !ignoreWidth) return s;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                char c = ch;
                if (ignoreWidth)
                {
                    if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);
                    else if (c == '　') c = ' ';
                }
                if (ignoreCase) c = char.ToLowerInvariant(c);
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>In-room search (50-SPEC 6장). Works on all items in the window, drawn or not.</summary>
    public sealed class SearchSession
    {
        private readonly List<int> _hits = new List<int>();
        private int _current = -1;

        public string Query { get; private set; }
        public IList<int> Hits { get { return _hits; } }
        public int CurrentItemIndex { get { return _current < 0 ? -1 : _hits[_current]; } }

        /// <summary>"3/12" style label; "0/0" when nothing matches.</summary>
        public string Label { get { return _hits.Count == 0 ? "0/0" : (_current + 1) + "/" + _hits.Count; } }

        public void Run(string query, IList<TimelineItem> items, Func<string, string> aliasOf, DisplayNameMode nameMode, bool ignoreCase, bool ignoreWidth)
        {
            Query = query ?? "";
            _hits.Clear();
            _current = -1;
            if (Query.Length == 0) return;
            var q = TextNormalizer.Normalize(Query, ignoreCase, ignoreWidth);
            for (int i = 0; i < items.Count; i++)
            {
                foreach (var text in SearchableTexts(items[i], aliasOf, nameMode))
                {
                    if (TextNormalizer.Normalize(text, ignoreCase, ignoreWidth).IndexOf(q, StringComparison.Ordinal) >= 0) { _hits.Add(i); break; }
                }
            }
            if (_hits.Count > 0) _current = _hits.Count - 1; // start at the newest match
        }

        public int Next() { if (_hits.Count == 0) return -1; _current = (_current + 1) % _hits.Count; return _hits[_current]; }
        public int Previous() { if (_hits.Count == 0) return -1; _current = (_current - 1 + _hits.Count) % _hits.Count; return _hits[_current]; }

        public static IEnumerable<string> SearchableTexts(TimelineItem item, Func<string, string> aliasOf, DisplayNameMode nameMode)
        {
            var mi = item as MessageItem;
            if (mi != null)
            {
                yield return mi.Message.Body;
                yield return mi.Message.SenderName;
                var a = aliasOf(mi.Message.SenderId);
                if (!string.IsNullOrEmpty(a)) yield return a;
                yield break;
            }
            var eg = item as EmoteGroupItem;
            if (eg != null)
            {
                foreach (var e in eg.Entries)
                {
                    yield return e.EmoteId;
                    yield return e.SenderName;
                    var a = aliasOf(e.SenderId);
                    if (!string.IsNullOrEmpty(a)) yield return a;
                }
            }
        }
    }

    /// <summary>Text export of the current room (50-SPEC 8장). Built from the same timeline items as the screen.</summary>
    public static class ConversationExporter
    {
        public static string Export(IList<TimelineItem> items, Func<string, string> aliasOf, TimeZoneInfo tz, Func<string, string> text)
        {
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.TimestampUtc, DateTimeKind.Utc), tz);
                var stamp = "[" + local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] ";
                var ds = item as DateSeparatorItem;
                if (ds != null) { sb.Append("── ").Append(ds.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" ──\n"); continue; }
                var mi = item as MessageItem;
                if (mi != null)
                {
                    sb.Append(stamp).Append(NameWithAlias(mi.Message.SenderName, aliasOf(mi.Message.SenderId))).Append(": ")
                      .Append(mi.Message.Body).Append('\n');
                    continue;
                }
                var eg = item as EmoteGroupItem;
                if (eg != null)
                {
                    sb.Append(stamp);
                    bool first = true;
                    foreach (var e in eg.Entries)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append(NameWithAlias(e.SenderName, aliasOf(e.SenderId))).Append(": [").Append(e.EmoteId).Append("] ×").Append(e.Count);
                    }
                    sb.Append('\n');
                    continue;
                }
                var mg = item as MeowGroupItem;
                if (mg != null)
                {
                    var last = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(mg.LastUtc, DateTimeKind.Utc), tz);
                    sb.Append(stamp).Append(string.Format(text("meow.summary"), mg.TotalCount, mg.PeopleLabel))
                      .Append(" (").Append(local.ToString("HH:mm", CultureInfo.InvariantCulture)).Append("–")
                      .Append(last.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(")\n");
                    continue;
                }
            }
            return sb.ToString();
        }

        /// <summary>Export always shows both names: "original (alias)".</summary>
        public static string NameWithAlias(string original, string alias)
        {
            return string.IsNullOrEmpty(alias) ? original : original + " (" + alias + ")";
        }
    }
}
