using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NyangTalk.Adapters.Storage;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.People;

namespace NyangTalk.Tests
{
    public static class StorageTests
    {
        [Test("90-7.1")]
        public static void SettingsPersistAcrossRestart()
        {
            var dir = Ctx.NewDir("settings");
            var p = new PreferencesService(new JsonDocumentStore(dir));
            p.Load();
            int changed = 0;
            foreach (var d in NyangTalk.Domain.Preferences.Settings.Defs)
            {
                var other = d.Choices != null ? d.Choices.FirstOrDefault(c => c != d.Default) : (d.Min.HasValue ? (d.Min.Value + d.Step).ToString() : "x");
                if (other == null) continue;
                A.True(p.Settings.Set(d.Key, other), "set " + d.Key);
                changed++;
            }
            var p2 = new PreferencesService(new JsonDocumentStore(dir));
            p2.Load();
            foreach (var d in NyangTalk.Domain.Preferences.Settings.Defs)
                A.Eq(p.Settings.Get(d.Key), p2.Settings.Get(d.Key), "persisted " + d.Key);
            A.True(changed >= 40, "all settings exercised (" + changed + ")");
        }

        [Test("90-7.2,60-5.8")]
        public static void SaveFailureKeepsChangeAndWarns()
        {
            var dir = Ctx.NewDir("savefail");
            var p = new PreferencesService(new JsonDocumentStore(dir));
            p.Load();
            var warnings = new List<string>();
            p.SaveFailed += k => warnings.Add(k);
            AtomicFile.FailureInjector = path => new IOException("locked (test)");
            int oldRetries = AtomicFile.Retries; AtomicFile.Retries = 1; AtomicFile.RetryDelayMs = 1;
            try
            {
                p.Settings.Set("screen.theme", "mint");
                A.Eq("mint", p.Settings.Get("screen.theme"), "applied");
                A.True(warnings.Contains("settings.saveFailed"), "settings warning");
                A.Eq(AliasError.None, p.SetAlias("765", "친구"), "alias set");
                A.Eq("친구", p.Aliases.Get("765"), "alias shown");
                A.True(warnings.Contains("people.saveFailed"), "alias warning");
            }
            finally { AtomicFile.FailureInjector = null; AtomicFile.Retries = oldRetries; AtomicFile.RetryDelayMs = 40; }
        }

        [Test("90-7.3")]
        public static void BrokenSettingsFile()
        {
            var dir = Ctx.NewDir("broken");
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not json");
            var p = new PreferencesService(new JsonDocumentStore(dir));
            p.Load();
            A.Eq("default", p.Settings.Get("screen.theme"), "defaults");
            A.True(p.BrokenSettingsCopy != null && File.Exists(p.BrokenSettingsCopy), "broken copy kept");
            A.True(Path.GetFileName(p.BrokenSettingsCopy).StartsWith("settings.json.broken-"), "name .broken-<date>");
            A.Eq("{ this is not json", File.ReadAllText(p.BrokenSettingsCopy), "original content preserved");
            A.Eq("ko", p.Lang, "language defaults to Korean (90-SPEC 3장, not from Windows)");
        }

        [Test("90-7.4")]
        public static void RetentionCleanup()
        {
            var dir = Ctx.NewDir("retention");
            var h = new FileHistoryStore(dir);
            var now = Fake.T0.AddDays(100);
            h.Append("A", "old room", new[] { Fake.Text("a", "very old", 0) });
            h.Append("B", "mixed", new[] { Fake.Text("a", "old", 0), new ChatMessage { Id = "n", SenderId = "a", SenderName = "a", Body = "new", TimestampUtc = now.AddDays(-1) } });
            A.Eq(0, h.Cleanup(now, 0), "forever: nothing removed");
            A.Eq(2, h.Cleanup(now, 30), "two old lines removed");
            A.False(h.ListRooms().Any(x => x.LobbyId == "A"), "empty room removed");
            A.Eq("new", h.LoadTail("B", 10).Single().Body, "recent kept");
        }

        [Test("50-4,90-3")]
        public static void TailReadAndRooms()
        {
            var dir = Ctx.NewDir("tail");
            var h = new FileHistoryStore(dir);
            var batch = Enumerable.Range(0, 20000).Select(i => Fake.Text(i % 3 == 0 ? "a" : "b", "message number " + i + " 가나다라", i)).ToList();
            h.Append("R", "큰 방", batch);
            var sw = Stopwatch.StartNew();
            var tail = h.LoadTail("R", 301);
            sw.Stop();
            A.Eq(301, tail.Count, "301 loaded");
            A.Eq("message number 19999 가나다라", tail.Last().Body, "newest last");
            long size = new FileInfo(Path.Combine(dir, "R.ndjson")).Length;
            A.True(h.LastTailBytesRead < size / 10, "read only the end (" + h.LastTailBytesRead + " of " + size + " bytes)");
            var rooms = h.ListRooms();
            A.Eq("큰 방", rooms.Single().Name, "room name");
            A.Eq(20000, rooms.Single().MessageCount, "count");
            h.DeleteRoom("R");
            A.Eq(0, h.ListRooms().Count, "deleted");
        }

        [Test("50-9.5,90-7.7")]
        public static void ThreeThousandLoadTime()
        {
            var dir = Ctx.NewDir("perf");
            var h = new FileHistoryStore(dir);
            h.Append("R", "perf", Enumerable.Range(0, 10000).Select(i => Fake.Text("p" + (i % 7), "성능 측정용 메시지 " + i, i)).ToList());
            GC.Collect(); GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            var tail = h.LoadTail("R", 3001);
            var w = new HistoryWindow();
            while (w.CanExpand(tail.Count)) w.Expand();
            var items = TimelineBuilder.Build(w.Select(tail), new TimelineOptions());
            sw.Stop();
            long after = GC.GetTotalMemory(false);
            A.Eq(3000, items.OfType<MessageItem>().Count(), "3000 messages");
            Ctx.Measurements.Add("3,000개 기록 읽기+묶기: " + sw.ElapsedMilliseconds + " ms (목표 1,500 ms, 화면 그리기 제외), 관리 힙 증가 약 " + ((after - before) / 1024 / 1024) + " MB");
            A.True(sw.ElapsedMilliseconds < 1500, "under 1.5 s: " + sw.ElapsedMilliseconds);
        }
    }
}
