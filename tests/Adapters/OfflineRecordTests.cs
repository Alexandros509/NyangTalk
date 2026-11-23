using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyangTalk.Adapters.GameExchange;
using NyangTalk.App.Demo;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public static class OfflineRecordTests
    {
        [Test("50-9.3,50-3.묶기")]
        public static void TextOrEmoteBetweenMeowsStartsNewGroup()
        {
            var t = Fake.Build(new[] { Fake.Text("a", "Meow Meow", 0), Fake.Text("b", "안녕", 10), Fake.Text("c", "야옹 야옹", 20) });
            A.Eq(2, t.OfType<MeowGroupItem>().Count(), "text in between: new group");
            var e = Fake.Build(new[] { Fake.Text("a", "Meow Meow", 0), Fake.Emote("b", "5", 10), Fake.Text("c", "야옹 야옹", 20) });
            A.Eq(2, e.OfType<MeowGroupItem>().Count(), "emote in between: new group");
            var exact = Fake.Build(new[] { Fake.Text("a", "Meow Meow", 0), Fake.Text("c", "야옹 야옹", 180) });
            A.Eq(1, exact.OfType<MeowGroupItem>().Count(), "exactly 3 min: same group");
        }

        /// <summary>40-SPEC 1.2: the component writes while the app is closed; on start the app shows them in order, once.</summary>
        [Test("40-1.2")]
        public static void MessagesWrittenWhileAppClosedAppearOnce()
        {
            var r = new Rig("offline");
            r.Step(); r.Step();
            r.Game.Say(2, "앱 켜져 있을 때", r.Clock.Now);
            r.Step();
            // app closes; the game keeps writing
            for (int i = 0; i < 5; i++) r.Game.Say(1 + i % 3, "앱이 꺼진 동안 " + i, r.Clock.Now.AddSeconds(10 + i));
            // app starts again on the same folders
            var again = new Rig2(r);
            again.Step(); again.Step();
            var bodies = again.Chat.Messages.Select(m => m.Body).ToList();
            A.Eq(1, bodies.Count(b => b == "앱 켜져 있을 때"), "no duplicate of the earlier message");
            for (int i = 0; i < 5; i++) A.Eq(1, bodies.Count(b => b == "앱이 꺼진 동안 " + i), "message " + i + " once");
            var order = bodies.Where(b => b.StartsWith("앱이 꺼진")).ToList();
            A.True(order.SequenceEqual(Enumerable.Range(0, 5).Select(i => "앱이 꺼진 동안 " + i)), "in order");
        }

        [Test("40-1.2")]
        public static void ComponentTrimDoesNotDuplicate()
        {
            var r = new Rig("trimdup");
            r.Step();
            var old = r.Clock.Now.AddDays(-40);
            r.Game.Say(2, "오래된 글", old);
            r.Game.Say(2, "최근 글", r.Clock.Now);
            r.Step();
            A.Eq(2, r.Chat.Messages.Count, "both moved to the records");
            // the game component trims its file (40 days > 30) while the app keeps running
            var file = Path.Combine(r.Dir, "exchange", Exchange.LobbyFile(r.Game.LobbyId));
            A.Eq(1, ExchangeRetention.TrimFile(file, r.Clock.Now, 30), "one expired line removed by the component");
            r.Game.Say(3, "다음 글", r.Clock.Now.AddSeconds(1));
            r.Step();
            var bodies = r.Chat.Messages.Select(m => m.Body).ToList();
            A.Eq(1, bodies.Count(b => b == "최근 글"), "not read twice after the file shrank");
            A.Eq(1, bodies.Count(b => b == "다음 글"), "new line read");
        }

        [Test("40-1.2,90-7.4")]
        public static void RetentionCleansExchangeAndRecordsForeverKeeps()
        {
            var r = new Rig("exretention");
            r.Step();
            var old = r.Clock.Now.AddDays(-40);
            r.Game.Say(2, "40일 전", old);
            r.Game.Say(2, "오늘", r.Clock.Now);
            r.Step();
            var file = Path.Combine(r.Dir, "exchange", Exchange.LobbyFile(r.Game.LobbyId));
            A.Eq(0, r.Exchange.TrimIdle(r.Clock.Now, 30, true), "game writing: app does not touch the file");
            A.Eq(0, r.Exchange.TrimIdle(r.Clock.Now, 0, false), "forever: nothing removed");
            A.Eq(1, r.Exchange.TrimIdle(r.Clock.Now, 30, false), "game idle: expired line removed from the exchange");
            A.False(File.ReadAllText(file).Contains("40일 전"), "gone from the exchange file");
            A.True(File.ReadAllText(file).Contains("오늘"), "recent kept");
            A.Eq(1, r.History.Cleanup(r.Clock.Now, 30), "and from the records");
            r.Exchange.WriteRetention(90);
            A.Eq(90, ExchangeRetention.ReadDays(Path.Combine(r.Dir, "exchange"), 30), "period handed to the component");
            r.Game.Say(3, "정리 뒤", r.Clock.Now.AddSeconds(1));
            r.Step();
            A.Eq(1, r.Chat.Messages.Count(m => m.Body == "정리 뒤"), "reading continues after trimming");
        }

        /// <summary>A second app start on the same folders as an earlier rig.</summary>
        private sealed class Rig2
        {
            public readonly Rig From;
            public readonly ChatService Chat;
            private readonly FileExchange _ex;
            public Rig2(Rig r)
            {
                From = r;
                _ex = new FileExchange(Path.Combine(r.Dir, "exchange"), r.Docs);
                Chat = new ChatService(_ex, r.History, r.Clock, new EventBus(), r.Prefs, r.Docs);
            }
            public void Step()
            {
                From.Clock.Now = From.Clock.Now.AddSeconds(1);
                From.Game.WriteStatus(From.Clock.Now);
                From.Game.WriteParticipants();
                Chat.Poll();
            }
        }
    }
}
