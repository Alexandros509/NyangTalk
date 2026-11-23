using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Domain.Conversation
{
    /// <summary>Emote grouping modes (50-SPEC 2장).</summary>
    public enum EmoteGroupMode
    {
        /// <summary>A1: window measured from the first emote of the same person + same emote.</summary>
        FixedFromFirst = 0,
        /// <summary>A2: window measured from the last emote of the same person + same emote.</summary>
        ExtendFromLast = 1,
        /// <summary>B: all emotes in the window on one line, regardless of person.</summary>
        AllOnOneLine = 2,
        /// <summary>C: one line per person for emotes in the window.</summary>
        PerPersonLine = 3
    }

    public sealed class TimelineOptions
    {
        public EmoteGroupMode EmoteMode = EmoteGroupMode.FixedFromFirst;
        public int EmoteWindowSeconds = 15;       // 15 / 30 / 180
        public bool HideMeows = false;
        public TimeZoneInfo TimeZone = TimeZoneInfo.Utc;
        public const int MeowGapSeconds = 180;
        public const int ContinuationSeconds = 60;
        public const int MeowPeopleCap = 128;
        public const int MeowTooltipPeople = 12;
    }

    public abstract class TimelineItem
    {
        /// <summary>Time used for ordering and date separators (first member for groups).</summary>
        public DateTime TimestampUtc;
        /// <summary>Stable key so the view can keep scroll anchors and focus across rebuilds.</summary>
        public abstract string Key { get; }
    }

    public sealed class DateSeparatorItem : TimelineItem
    {
        public DateTime LocalDate;
        public override string Key { get { return "date:" + LocalDate.ToString("yyyy-MM-dd"); } }
    }

    public sealed class MessageItem : TimelineItem
    {
        public ChatMessage Message;
        /// <summary>Same sender within 1 minute after a previous message item: hide name/portrait (50-SPEC 1장).</summary>
        public bool IsContinuation;
        public override string Key { get { return "msg:" + Message.Id; } }
    }

    public sealed class EmoteCount
    {
        public string SenderId;
        public string SenderName;
        public string EmoteId;
        public int Count;
    }

    /// <summary>Grouped emotes. For A1/A2 there is one entry; B has entries for many people; C has one person.</summary>
    public sealed class EmoteGroupItem : TimelineItem
    {
        public EmoteGroupMode Mode;
        public readonly List<EmoteCount> Entries = new List<EmoteCount>();
        public readonly List<ChatMessage> Members = new List<ChatMessage>();
        public DateTime AnchorUtc;
        public DateTime LastUtc;
        public bool IsMine;
        public int TotalCount { get { return Members.Count; } }
        public override string Key { get { return "emote:" + Members[0].Id; } }

        internal void Add(ChatMessage m)
        {
            Members.Add(m);
            LastUtc = m.TimestampUtc;
            if (!m.IsMine) IsMine = false;
            EmoteCount e = null;
            foreach (var x in Entries)
            {
                if (x.EmoteId == m.EmoteId && x.SenderId == m.SenderId) { e = x; break; }
            }
            if (e == null)
            {
                e = new EmoteCount { SenderId = m.SenderId, SenderName = m.SenderName, EmoteId = m.EmoteId };
                Entries.Add(e);
            }
            e.Count++;
        }
    }

    public sealed class MeowGroupItem : TimelineItem
    {
        public readonly List<ChatMessage> Members = new List<ChatMessage>();
        private readonly Dictionary<string, int> _perPerson = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private bool _peopleOverflow;
        public DateTime FirstUtc { get { return Members[0].TimestampUtc; } }
        public DateTime LastUtc { get { return Members[Members.Count - 1].TimestampUtc; } }
        public int TotalCount { get { return Members.Count; } }
        public override string Key { get { return "meow:" + Members[0].Id; } }

        /// <summary>Distinct people, counted up to 128 (50-SPEC 3장).</summary>
        public int PeopleCount { get { return _perPerson.Count; } }
        public bool PeopleOverflow { get { return _peopleOverflow; } }
        public string PeopleLabel { get { return _peopleOverflow ? TimelineOptions.MeowPeopleCap + "+" : _perPerson.Count.ToString(); } }

        internal void Add(ChatMessage m)
        {
            Members.Add(m);
            var id = m.SenderId ?? "";
            int c;
            if (_perPerson.TryGetValue(id, out c)) { _perPerson[id] = c + 1; return; }
            if (_perPerson.Count >= TimelineOptions.MeowPeopleCap) { _peopleOverflow = true; return; }
            _perPerson[id] = 1;
            _names[id] = m.SenderName;
        }

        /// <summary>Per-person counts for the tooltip: by count desc, at most 12.</summary>
        public List<EmoteCount> TopPeople()
        {
            return _perPerson.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Take(TimelineOptions.MeowTooltipPeople)
                .Select(p => new EmoteCount { SenderId = p.Key, SenderName = _names[p.Key], Count = p.Value }).ToList();
        }
    }

    /// <summary>Builds display items from raw messages. Pure and deterministic; rebuilt whenever input changes.</summary>
    public static class TimelineBuilder
    {
        public static List<TimelineItem> Build(IList<ChatMessage> messages, TimelineOptions opt)
        {
            var body = new List<TimelineItem>();
            var openEmote = new Dictionary<string, EmoteGroupItem>();
            MeowGroupItem openMeow = null;
            var window = TimeSpan.FromSeconds(opt.EmoteWindowSeconds);
            var meowGap = TimeSpan.FromSeconds(TimelineOptions.MeowGapSeconds);

            // stable order by time
            var ordered = messages.Select((m, i) => new { m, i }).OrderBy(x => x.m.TimestampUtc).ThenBy(x => x.i).Select(x => x.m).ToList();

            foreach (var m in ordered)
            {
                if (!m.IsEmote) openEmote.Clear(); // 50-SPEC 2장: text or a meow in between ends every open emote group
                if (!MeowCatalog.IsMeow(m)) openMeow = null; // 50-SPEC 3장: text or an emote in between ends the meow group
                if (MeowCatalog.IsMeow(m))
                {
                    if (openMeow != null && m.TimestampUtc - openMeow.LastUtc <= meowGap) { openMeow.Add(m); continue; }
                    openMeow = new MeowGroupItem { TimestampUtc = m.TimestampUtc };
                    openMeow.Add(m);
                    body.Add(openMeow);
                    continue;
                }
                if (m.IsEmote)
                {
                    string key;
                    switch (opt.EmoteMode)
                    {
                        case EmoteGroupMode.AllOnOneLine: key = "all"; break;
                        case EmoteGroupMode.PerPersonLine: key = "p:" + m.SenderId; break;
                        default: key = "pe:" + m.SenderId + "\u0001" + m.EmoteId; break;
                    }
                    EmoteGroupItem g;
                    if (openEmote.TryGetValue(key, out g))
                    {
                        var basis = opt.EmoteMode == EmoteGroupMode.ExtendFromLast ? g.LastUtc : g.AnchorUtc;
                        if (m.TimestampUtc - basis <= window) { g.Add(m); continue; }
                    }
                    g = new EmoteGroupItem { Mode = opt.EmoteMode, TimestampUtc = m.TimestampUtc, AnchorUtc = m.TimestampUtc, IsMine = m.IsMine };
                    g.Add(m);
                    openEmote[key] = g;
                    body.Add(g);
                    continue;
                }
                body.Add(new MessageItem { Message = m, TimestampUtc = m.TimestampUtc });
            }

            if (opt.HideMeows) body = body.Where(i => !(i is MeowGroupItem)).ToList();

            // Date separators and continuation flags.
            var result = new List<TimelineItem>(body.Count + 8);
            DateTime? lastDate = null;
            MessageItem prevMsg = null;
            foreach (var item in body)
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.TimestampUtc, DateTimeKind.Utc), opt.TimeZone).Date;
                if (lastDate == null || local != lastDate.Value)
                {
                    result.Add(new DateSeparatorItem { LocalDate = local, TimestampUtc = item.TimestampUtc });
                    lastDate = local;
                    prevMsg = null;
                }
                var mi = item as MessageItem;
                if (mi != null)
                {
                    mi.IsContinuation = prevMsg != null && prevMsg.Message.SenderId == mi.Message.SenderId
                        && prevMsg.Message.IsMine == mi.Message.IsMine
                        && (mi.Message.TimestampUtc - prevMsg.Message.TimestampUtc) <= TimeSpan.FromSeconds(TimelineOptions.ContinuationSeconds);
                    prevMsg = mi;
                }
                else prevMsg = null;
                result.Add(item);
            }
            return result;
        }
    }
}
