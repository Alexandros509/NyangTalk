using System;
using System.IO;
using System.Linq;
using NyangTalk.Adapters.Storage;
using NyangTalk.App;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Tests
{
    /// <summary>95-SPEC 2.3 resend confirmation, 2.4 health signal, 2.5 log file.</summary>
    public static class HealthLogResendTests
    {
        private static Row OutRow(UiRig rig, string text)
        {
            rig.Form.RefreshChat();
            return rig.Form.Chat.Rows.Single(r => r.Outgoing != null && r.Outgoing.Text == text);
        }

        private static string[] Actions(Row r) { return r.Parts.Where(p => p.Kind == PartKind.Button).Select(p => p.Action).OrderBy(a => a).ToArray(); }

        private static int Requests(UiRig rig) { var d = Path.Combine(rig.Dir, "exchange", "outbox"); return Directory.Exists(d) ? Directory.GetFiles(d, "*.json").Length : 0; }

        [Test("95-2.3")]
        public static void UnconfirmedResendNeedsASecondPressRejectedDoesNot()
        {
            using (var rig = new UiRig("resend", 3))
            {
                var f = rig.Form;
                A.Eq(SendBlock.None, rig.Host.Chat.Send("결과 모르는 글"), "sent");
                // the game stops answering but keeps writing status: after 10 s the result is unknown
                for (int i = 0; i < 11; i++) { rig.Clock.Now = rig.Clock.Now.AddSeconds(1); rig.Game.WriteStatus(rig.Clock.Now); f.Tick(); }
                var row = OutRow(rig, "결과 모르는 글");
                A.Eq(OutgoingState.Unconfirmed, row.Outgoing.State, "확인 못 함");
                A.Eq("delete,restore,retry", string.Join(",", Actions(row)), "다시 보내기 / 원문 되살리기 / 지우기");
                var id = row.Outgoing.RequestId;
                int before = Requests(rig);

                f.OnChatAction("retry", row);
                row = OutRow(rig, "결과 모르는 글");
                A.Eq(id, row.Outgoing.RequestId, "not sent yet: same entry");
                A.Eq(before, Requests(rig), "no request written by the first press");
                A.Eq("resendCancel,resendConfirm", string.Join(",", Actions(row)), "그래도 보내기 / 취소 under the message");
                A.True(row.Parts.Any(p => p.Kind == PartKind.Status && p.Text == rig.Host.Prefs.L("send.resendAsk")), "says it may already be delivered");
                foreach (var w in new[] { 300, 510 }) A.Eq(0, RowChecks.Check(rig.Form.Chat.Layout2.OutgoingRow(row.Outgoing, w, f.CurrentStyle), w).Count, w + ": the question fits");

                f.OnChatAction("resendCancel", row);
                row = OutRow(rig, "결과 모르는 글");
                A.Eq(OutgoingState.Unconfirmed, row.Outgoing.State, "cancel keeps the state");
                A.Eq("delete,restore,retry", string.Join(",", Actions(row)), "back to 다시 보내기 / 원문 되살리기 / 지우기");
                A.Eq(before, Requests(rig), "cancel sends nothing");

                f.OnChatAction("retry", row);
                f.OnChatAction("resendConfirm", OutRow(rig, "결과 모르는 글"));
                A.Eq(before + 1, Requests(rig), "그래도 보내기 sends");
                var again = rig.Host.Chat.Outbox.Pending.Single(p => p.Text == "결과 모르는 글");
                A.True(again.RequestId != id && again.State == OutgoingState.Sending, "a new send");

                // rejected: sent again at once
                rig.Step(); rig.Step();
                rig.Game.RejectNext = true;
                rig.Clock.Now = rig.Clock.Now.AddSeconds(2);
                rig.Host.Chat.Send("거절될 글");
                rig.Step(); rig.Step();
                row = OutRow(rig, "거절될 글");
                A.Eq(OutgoingState.Rejected, row.Outgoing.State, "rejected");
                before = Requests(rig);
                f.OnChatAction("retry", row);
                A.Eq(before + 1, Requests(rig), "rejected: one press sends");
            }
        }

        [Test("95-2.4")]
        public static void HealthWarnsOnlyWhenTheAppMissesWhatTheComponentGot()
        {
            var t = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
            var w = new ReceiveWatch();
            long ev = 100, app = 0;
            // normal flow: each event is read; the status count may lag one behind
            for (int i = 0; i < 120; i++) { ev++; app++; A.False(w.Observe(t.AddSeconds(i), true, "s|L", ev - (i % 2), app), "normal flow " + i); }
            // quiet room for a long time
            for (int i = 120; i < 300; i++) A.False(w.Observe(t.AddSeconds(i), true, "s|L", ev, app), "quiet room");
            // the last read saw a count one behind (ev - 1); then one line is lost: two above that, no warning
            A.False(w.Observe(t.AddSeconds(300), true, "s|L", ev + 1, app), "late count plus one lost");
            A.False(w.Observe(t.AddSeconds(400), true, "s|L", ev + 1, app), "long after: still not a warning");
            // receiving stopped: the component got three more, the app nothing for 20 s
            w.Reset();
            A.False(w.Observe(t, true, "s|L", ev, app), "start");
            A.False(w.Observe(t.AddSeconds(5), true, "s|L", ev + 3, app), "not before 20 s");
            A.False(w.Observe(t.AddSeconds(19), true, "s|L", ev + 3, app), "19 s");
            A.True(w.Observe(t.AddSeconds(20), true, "s|L", ev + 3, app), "20 s: warning");
            A.False(w.Observe(t.AddSeconds(21), true, "s|L", ev + 4, app + 1), "a line read clears it");
            A.False(w.Observe(t.AddSeconds(60), false, "s|L", ev + 9, app + 1), "disconnected: not judged");
            A.False(w.Observe(t.AddSeconds(90), true, "s|L", null, app + 1), "older component without the count: not judged");
            w.Observe(t.AddSeconds(100), true, "s|L", 50, app + 1);
            A.False(w.Observe(t.AddSeconds(130), true, "s2|L", 60, app + 1), "a new game run starts over");

            // end to end with the fake game and the app window
            using (var rig = new UiRig("health", 3))
            {
                var chat = rig.Host.Chat;
                for (int i = 0; i < 40; i++) { if (i % 3 == 0) rig.Game.Say(1, "평소 대화 " + i, rig.Clock.Now); rig.Step(); }
                Shell.Link.UpdateNotices();
                A.False(chat.Receive.Warning, "normal chat: no warning");
                A.True(rig.Form.Notices.Get(NyangTalk.App.NoticeKind.Health) == null, "no health notice");
                for (int i = 0; i < 40; i++) rig.Step();
                A.False(chat.Receive.Warning, "quiet room: no warning");

                rig.Game.DropLines = true;   // the hook gets chat, but nothing reaches the app
                for (int i = 0; i < 25; i++) { if (i < 4) rig.Game.Say(2, "사라질 글 " + i, rig.Clock.Now); rig.Step(); }
                Shell.Link.UpdateNotices();
                A.True(chat.Receive.Warning, "receiving stopped: warning");
                var n = rig.Form.Notices.Get(NyangTalk.App.NoticeKind.Health);
                A.True(n != null && n.ActionText == rig.Host.Prefs.L("notice.btn.reconnect"), "⚠ with 다시 연결 on the notice line");
                A.Eq(1, rig.Host.Log.Count(l => l.Contains("health:")), "logged once");
                n.Action();
                Shell.Link.UpdateNotices();
                A.False(chat.Receive.Warning, "다시 연결 starts over");
                A.True(rig.Form.Notices.Get(NyangTalk.App.NoticeKind.Health) == null, "notice gone");

                rig.Game.DropLines = false; rig.Game.ReportEvents = false;
                rig.Game.DropLines = true;
                for (int i = 0; i < 30; i++) { if (i < 5) rig.Game.Say(2, "옛 구성요소 " + i, rig.Clock.Now); rig.Step(); }
                A.False(chat.Receive.Warning, "component without the count: never warns");
            }
        }

        [Test("95-2.5")]
        public static void LogFileRotatesAndKeepsNoPrivateData()
        {
            var dir = Ctx.NewDir("logfile");
            var log = new LogFile(dir) { Limit = 1000 };
            for (int i = 0; i < 200; i++) A.True(log.Write("line " + i.ToString("D3") + " " + new string('x', 40)), "written");
            A.True(File.Exists(log.Path) && File.Exists(log.OldPath(1)) && File.Exists(log.OldPath(2)), "current and two earlier files");
            A.False(File.Exists(log.OldPath(3)), "no third earlier file");
            foreach (var p in new[] { log.Path, log.OldPath(1), log.OldPath(2) }) A.True(new FileInfo(p).Length <= 1000, Path.GetFileName(p) + " within the size");
            var tail = log.Tail(20);
            A.Eq(20, tail.Count, "20 lines");
            A.True(tail.Last().StartsWith("line 199"), "the newest last: " + tail.Last());
            A.True(tail.First().StartsWith("line 180"), "in order: " + tail.First());

            using (var rig = new UiRig("log-private", 6))
            {
                var host = rig.Host;
                host.UserProfile = @"C:\Users\sample-user";
                var msg = host.Chat.Messages.Last(m => !m.IsMine);
                var someone = rig.Game.Members[2];
                host.Prefs.Aliases.Set(someone[0], "단짝친구");
                host.AddLog("send failed: \"" + msg.Body + "\" from " + msg.SenderName + " / " + someone[1] + " (단짝친구) id " + someone[0] + " in C:\\Users\\sample-user\\AppData\\Local\\NyangTalk");
                host.AddLog("emote from " + someone[1]);
                var text = File.ReadAllText(host.LogFile.Path);
                foreach (var secret in new[] { msg.Body, msg.SenderName, someone[1], "단짝친구", someone[0], @"C:\Users\sample-user" })
                    A.False(text.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0, "not in the log: " + secret + "\n" + text);
                A.True(text.Contains("…" + someone[0].Substring(someone[0].Length - 4)) && text.Contains("%USERPROFILE%") && text.Contains(NyangTalk.Domain.Preferences.DiagnosticsSanitizer.Hidden), "masked, not dropped: " + text);
                A.False(host.Log.Any(l => l.Contains(msg.Body)), "not in the in-memory list either");
                for (int i = 0; i < 30; i++) host.AddLog("filler " + i);
                var diag = Shell.Link.Diagnostics();
                var after = diag.Substring(diag.IndexOf("recent log:", StringComparison.Ordinal));
                A.Eq(21, after.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length, "진단 내용 has the last 20 log lines");
                A.True(after.Contains("filler 29") && after.Contains("filler 10" + Environment.NewLine) && !after.Contains("filler 9" + Environment.NewLine), "the newest ones");
            }
        }
    }
}
