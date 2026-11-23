using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Tests
{
    public static class Fake
    {
        public static readonly DateTime T0 = new DateTime(2026, 11, 19, 3, 0, 0, DateTimeKind.Utc);
        private static int _n;

        public static ChatMessage Text(string sender, string body, double seconds, bool mine = false)
        {
            return new ChatMessage { Id = "m" + (++_n), LobbyId = "L1", SenderId = sender, SenderName = "name-" + sender, Body = body, Kind = MessageKind.Text, TimestampUtc = T0.AddSeconds(seconds), IsMine = mine };
        }

        public static ChatMessage Emote(string sender, string emote, double seconds, bool mine = false)
        {
            return new ChatMessage { Id = "m" + (++_n), LobbyId = "L1", SenderId = sender, SenderName = "name-" + sender, Body = emote, Kind = MessageKind.Emote, EmoteId = emote, TimestampUtc = T0.AddSeconds(seconds), IsMine = mine };
        }

        public static List<TimelineItem> Build(IList<ChatMessage> ms, EmoteGroupMode mode = EmoteGroupMode.FixedFromFirst, int window = 15, bool hideMeows = false)
        {
            return TimelineBuilder.Build(ms, new TimelineOptions { EmoteMode = mode, EmoteWindowSeconds = window, HideMeows = hideMeows });
        }

        public static List<T> Of<T>(IEnumerable<TimelineItem> items) where T : TimelineItem { return items.OfType<T>().ToList(); }
    }

    public static class ConversationTests
    {
        [Test("50-9.1")]
        public static void BodyIsKeptVerbatim()
        {
            var body = "한글 😺 이모지\n둘째 줄 <b>굵게</b> &amp; \t탭";
            var items = Fake.Build(new[] { Fake.Text("a", body, 0) });
            var m = Fake.Of<MessageItem>(items).Single();
            A.Eq(body, m.Message.Body, "timeline body");
            var export = ConversationExporter.Export(items, id => null, TimeZoneInfo.Utc, k => Strings.Get(k, "ko"));
            A.True(export.Contains(body), "export contains body verbatim");
        }

        [Test("50-9.2")]
        public static void SevenSameEmotesGroupToOneLine()
        {
            var ms = Enumerable.Range(0, 7).Select(i => Fake.Emote("a", "heart", i)).ToList();
            var groups = Fake.Of<EmoteGroupItem>(Fake.Build(ms));
            A.Eq(1, groups.Count, "one group");
            A.Eq(7, groups[0].TotalCount, "×7");
            A.Eq(7, groups[0].Entries.Single().Count, "entry count");
        }

        [Test("50-9.2")]
        public static void TextOrMeowInBetweenSplitsGroup()
        {
            // 50-SPEC 2장 (2026-11-19 정정): plain text or a meow in between ends the group, even inside the window.
            var text = new List<ChatMessage> { Fake.Emote("a", "heart", 0), Fake.Emote("a", "heart", 1), Fake.Text("b", "hi", 2), Fake.Emote("a", "heart", 3) };
            var g = Fake.Of<EmoteGroupItem>(Fake.Build(text));
            A.Eq(2, g.Count, "text splits");
            A.Eq(2, g[0].TotalCount, "first part ×2");
            var meow = new List<ChatMessage> { Fake.Emote("a", "heart", 0), Fake.Text("c", "Meow Meow", 1), Fake.Emote("a", "heart", 2) };
            A.Eq(2, Fake.Of<EmoteGroupItem>(Fake.Build(meow)).Count, "meow splits");
            foreach (EmoteGroupMode mode in Enum.GetValues(typeof(EmoteGroupMode)))
                A.Eq(2, Fake.Of<EmoteGroupItem>(Fake.Build(text, mode)).Count, "text splits in " + mode);
            // only another person's emote in between: no split
            var other = new List<ChatMessage> { Fake.Emote("a", "heart", 0), Fake.Emote("b", "star", 1), Fake.Emote("a", "heart", 2) };
            var go = Fake.Of<EmoteGroupItem>(Fake.Build(other)).Where(x => x.Entries[0].SenderId == "a").ToList();
            A.Eq(1, go.Count, "other emote does not split");
            A.Eq(2, go[0].TotalCount, "×2");
        }

        [Test("50-9.2")]
        public static void EmoteWindowBoundariesPerMode()
        {
            // A1: fixed from first. 0, 10, 20 → 20 is 20 s after first → new group.
            var ms = new List<ChatMessage> { Fake.Emote("a", "x", 0), Fake.Emote("a", "x", 10), Fake.Emote("a", "x", 20) };
            A.Eq(2, Fake.Of<EmoteGroupItem>(Fake.Build(ms, EmoteGroupMode.FixedFromFirst)).Count, "A1 splits after 15 s from first");
            // A2: extend from last → 0,10,20 stays one group.
            A.Eq(1, Fake.Of<EmoteGroupItem>(Fake.Build(ms, EmoteGroupMode.ExtendFromLast)).Count, "A2 extends");
            // 15 s inclusive, 16 s splits.
            var edge = new List<ChatMessage> { Fake.Emote("a", "x", 0), Fake.Emote("a", "x", 15), Fake.Emote("a", "x", 31) };
            A.Eq(2, Fake.Of<EmoteGroupItem>(Fake.Build(edge)).Count, "15 s inclusive");
            // window 30 / 180
            A.Eq(1, Fake.Of<EmoteGroupItem>(Fake.Build(ms, EmoteGroupMode.FixedFromFirst, 30)).Count, "30 s window");
            var far = new List<ChatMessage> { Fake.Emote("a", "x", 0), Fake.Emote("a", "x", 170) };
            A.Eq(1, Fake.Of<EmoteGroupItem>(Fake.Build(far, EmoteGroupMode.FixedFromFirst, 180)).Count, "180 s window");
            // different person or emote never merge in A1
            var diff = new List<ChatMessage> { Fake.Emote("a", "x", 0), Fake.Emote("b", "x", 1), Fake.Emote("a", "y", 2) };
            A.Eq(3, Fake.Of<EmoteGroupItem>(Fake.Build(diff)).Count, "A1 keys by person+emote");
        }

        [Test("50-9.2")]
        public static void ModesBAndC()
        {
            var ms = new List<ChatMessage> { Fake.Emote("a", "x", 0), Fake.Emote("b", "x", 1), Fake.Emote("a", "y", 2), Fake.Emote("a", "x", 3) };
            var b = Fake.Of<EmoteGroupItem>(Fake.Build(ms, EmoteGroupMode.AllOnOneLine));
            A.Eq(1, b.Count, "B: one line");
            A.Eq(4, b[0].TotalCount, "B total");
            A.Eq(3, b[0].Entries.Count, "B entries a:x, b:x, a:y");
            var c = Fake.Of<EmoteGroupItem>(Fake.Build(ms, EmoteGroupMode.PerPersonLine));
            A.Eq(2, c.Count, "C: per person");
            A.Eq(3, c.First(g => g.Entries[0].SenderId == "a").TotalCount, "C a total");
        }

        [Test("50-9.3")]
        public static void MeowListMatchesThePhraseFixture()
        {
            var phrases = File.ReadAllLines(Path.Combine(Ctx.Root, "tests", "fixtures", "meow-phrases.txt"));
            A.Eq(19, phrases.Length, "19 supported phrases");
            A.True(phrases.SequenceEqual(MeowCatalog.Phrases), "catalog equals the independent fixture byte-for-byte");
        }

        [Test("50-9.3")]
        public static void MeowExactMatchOnly()
        {
            foreach (var p in MeowCatalog.Phrases) A.True(MeowCatalog.IsMeow(Fake.Text("a", p, 0)), "meow: " + p);
            foreach (var p in new[] { "야옹 야옹!", "우리 고양이가 야옹 야옹 하네", " 야옹 야옹", "야옹 야옹 ", "야옹 야옹\n", "meow meow", "MEOW MEOW" })
                A.False(MeowCatalog.IsMeow(Fake.Text("a", p, 0)), "not meow: [" + p + "]");
            A.False(MeowCatalog.IsMeow(Fake.Emote("a", "Meow Meow", 0)), "emote with meow name is not meow");
        }

        [Test("50-9.3")]
        public static void MeowGapBoundary()
        {
            var ms = new List<ChatMessage> { Fake.Text("a", "Meow Meow", 0), Fake.Text("b", "야옹 야옹", 179), Fake.Text("c", "Мяу Мяу", 179 + 181) };
            var g = Fake.Of<MeowGroupItem>(Fake.Build(ms));
            A.Eq(2, g.Count, "2:59 groups, 3:01 splits");
            A.Eq(2, g[0].TotalCount, "first group 2");
            A.Eq("2", g[0].PeopleLabel, "two people");
        }

        [Test("50-9.3")]
        public static void MeowPeopleCapAndTooltip()
        {
            var ms = new List<ChatMessage>();
            for (int i = 0; i < 140; i++) ms.Add(Fake.Text("p" + i, "Meow Meow", i));
            for (int i = 0; i < 5; i++) ms.Add(Fake.Text("p3", "Meow Meow", 140 + i));
            var g = Fake.Of<MeowGroupItem>(Fake.Build(ms)).Single();
            A.Eq(145, g.TotalCount, "total keeps counting");
            A.Eq("128+", g.PeopleLabel, "people capped at 128+");
            var top = g.TopPeople();
            A.Eq(12, top.Count, "tooltip max 12");
            A.Eq("p3", top[0].SenderId, "sorted by count");
            A.Eq(6, top[0].Count, "p3 count");
        }

        [Test("50-9.3")]
        public static void MeowsDoNotPushOutNormalMessages()
        {
            var ms = new List<ChatMessage>();
            int t = 0;
            for (int i = 0; i < 300; i++) { ms.Add(Fake.Text("a", "normal " + i, t++)); if (i % 3 == 0) ms.Add(Fake.Text("b", "Meow Meow", t++)); }
            for (int i = 0; i < 100; i++) ms.Add(Fake.Text("b", "Meow Meow", t++));
            A.Eq(200, ms.Count(MeowCatalog.IsMeow), "200 meows");
            var w = new HistoryWindow();
            var sel = w.Select(ms);
            A.Eq(300, sel.Count(HistoryWindow.Counts), "all 300 normal visible");
            var visible = Fake.Of<MessageItem>(Fake.Build(sel)).Count;
            A.Eq(300, visible, "300 message items");
            var hidden = Fake.Build(sel, hideMeows: true);
            A.Eq(0, Fake.Of<MeowGroupItem>(hidden).Count, "hide meows removes groups");
        }

        [Test("50-9.4")]
        public static void HistoryWindowSteps()
        {
            var w = new HistoryWindow();
            var seen = new List<int> { w.Limit };
            while (w.CanExpand(5000)) { w.Expand(); seen.Add(w.Limit); }
            A.Eq("300,800,1300,1800,2300,2800,3000", string.Join(",", seen), "steps");
            w.Collapse();
            A.Eq(300, w.Limit, "collapse");
            A.False(new HistoryWindow().CanExpand(299), "room under 300: no button");
            A.False(new HistoryWindow().CanExpand(300), "exactly 300: nothing older");
            A.True(new HistoryWindow().CanExpand(301), "301: button");
            var ms = Enumerable.Range(0, 1000).Select(i => Fake.Text("a", "m" + i, i)).ToList();
            var w2 = new HistoryWindow(); w2.Expand();
            var sel = w2.Select(ms);
            A.Eq(800, sel.Count, "800 selected");
            A.Eq("m999", sel.Last().Body, "newest kept");
            A.Eq("m200", sel.First().Body, "oldest dropped first");
        }

        [Test("50-1")]
        public static void ContinuationAndDateSeparators()
        {
            var ms = new List<ChatMessage> { Fake.Text("a", "1", 0), Fake.Text("a", "2", 59), Fake.Text("a", "3", 59 + 61), Fake.Text("b", "4", 125), Fake.Text("b", "5", 86400) };
            var items = Fake.Build(ms);
            var msgs = Fake.Of<MessageItem>(items);
            A.False(msgs[0].IsContinuation, "first");
            A.True(msgs[1].IsContinuation, "within 1 min");
            A.False(msgs[2].IsContinuation, "after 61 s");
            A.False(msgs[3].IsContinuation, "different person");
            A.False(msgs[4].IsContinuation, "new day");
            A.Eq(2, Fake.Of<DateSeparatorItem>(items).Count, "two date separators");
        }

        [Test("50-9.6,60-5.1")]
        public static void SearchCyclesAndFindsAliases()
        {
            var ms = Enumerable.Range(0, 3000).Select(i => Fake.Text(i % 2 == 0 ? "a" : "b", i % 1000 == 0 ? "Hello ＷＯＲＬＤ " + i : "x" + i, i)).ToList();
            var items = Fake.Build(ms);
            var s = new SearchSession();
            s.Run("world", items, id => null, DisplayNameMode.AliasOnly, true, true);
            A.Eq(3, s.Hits.Count, "full-width + case folded hits");
            A.Eq("3/3", s.Label, "starts at newest");
            int first = s.Next();
            A.Eq("1/3", s.Label, "wraps to first");
            A.Eq(s.Hits[0], first, "index of first hit");
            A.True(first < 50, "hit far above the drawn area is still found");
            s.Previous();
            A.Eq("3/3", s.Label, "wraps back");
            s.Run("WORLD", items, id => null, DisplayNameMode.AliasOnly, false, false);
            A.Eq(0, s.Hits.Count, "strict mode does not fold width");
            s.Run("고양이", items, id => id == "b" ? "고양이친구" : null, DisplayNameMode.AliasOnly, true, true);
            A.Eq(1500, s.Hits.Count, "alias search");
        }

        [Test("50-9.10")]
        public static void ExportIncludesHiddenMeowSummary()
        {
            var ms = new List<ChatMessage> { Fake.Text("a", "안녕", 0), Fake.Text("b", "Meow Meow", 1), Fake.Text("c", "야옹 야옹", 2), Fake.Emote("a", "heart", 3), Fake.Emote("a", "heart", 4) };
            var forExport = TimelineBuilder.Build(ms, new TimelineOptions { HideMeows = false });
            var text = ConversationExporter.Export(forExport, id => id == "a" ? "에이" : null, TimeZoneInfo.Utc, k => Strings.Get(k, "ko"));
            A.True(text.Contains("name-a (에이): 안녕"), "original and alias");
            A.True(text.Contains("야옹 2회 · 2명"), "meow summary: " + text);
            A.True(text.Contains("[heart] ×2"), "emote ×N");
            // Screen with hidden meows shows the same message lines.
            var screen = TimelineBuilder.Build(ms, new TimelineOptions { HideMeows = true });
            foreach (var mi in Fake.Of<MessageItem>(screen)) A.True(text.Contains(mi.Message.Body), "screen line in export");
        }

        [Test("70-3.1,70-3.2,70-3.3")]
        public static void NotificationRules()
        {
            var other = Fake.Text("b", "hi", 0);
            foreach (NotifyMode mode in Enum.GetValues(typeof(NotifyMode)))
            {
                var u = new UnreadTracker { Mode = mode };
                u.OnMessage(other, false, false, true);
                if (mode == NotifyMode.Off) { A.False(u.Flashing, "off: no flash"); A.Eq(0, u.Unread, "off: no badge"); }
                if (mode == NotifyMode.Flash) { A.True(u.Flashing, "flash"); A.Eq(0, u.Unread, "flash: no badge"); }
                if (mode == NotifyMode.FlashAndBadge) { A.True(u.Flashing, "both flash"); A.Eq(1, u.Unread, "both badge"); }
            }
            var t = new UnreadTracker();
            t.OnMessage(other, false, false, false);
            t.OnActivated(false);
            A.False(t.Flashing, "activation stops flash");
            A.Eq(1, t.Unread, "badge stays while reading above");
            t.OnScrolled(true, true);
            A.Eq(0, t.Unread, "bottom clears badge");
            var q = new UnreadTracker();
            q.OnMessage(Fake.Text("me", "x", 0, true), false, false, false);
            q.OnMessage(Fake.Text("b", "Meow Meow", 0), false, false, false);
            q.OnMessage(other, true, false, false);
            A.Eq(0, q.Unread, "mine/meow/initial history do not notify");
            A.False(q.Flashing, "no flash either");
        }
    }
}
