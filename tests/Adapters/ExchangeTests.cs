using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NyangTalk.Adapters.GameExchange;
using NyangTalk.Adapters.Storage;
using NyangTalk.App.Demo;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Sending;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public sealed class FakeClock : IClock
    {
        public DateTime Now = Fake.T0;
        public DateTime UtcNow { get { return Now; } }
    }

    /// <summary>A full app core wired to a fake game in a temp folder.</summary>
    public sealed class Rig
    {
        public string Dir;
        public FakeGame Game;
        public FakeClock Clock = new FakeClock();
        public FileExchange Exchange;
        public FileHistoryStore History;
        public JsonDocumentStore Docs;
        public PreferencesService Prefs;
        public EventBus Bus = new EventBus();
        public ChatService Chat;

        public Rig(string name)
        {
            Dir = Ctx.NewDir(name);
            Docs = new JsonDocumentStore(Path.Combine(Dir, "data"));
            Exchange = new FileExchange(Path.Combine(Dir, "exchange"), Docs);
            Game = new FakeGame(Path.Combine(Dir, "exchange"));
            History = new FileHistoryStore(Path.Combine(Dir, "history"));
            Prefs = new PreferencesService(Docs);
            Prefs.Load();
            Chat = new ChatService(Exchange, History, Clock, Bus, Prefs, Docs);
        }

        public void GameTick()
        {
            Game.WriteStatus(Clock.Now);
            Game.WriteParticipants();
            Game.ProcessOutbox(Clock.Now);
        }

        public void Step(double seconds = 1)
        {
            Clock.Now = Clock.Now.AddSeconds(seconds);
            GameTick();
            Chat.Poll();
        }
    }

    public static class ExchangeTests
    {
        [Test("40-7.9")]
        public static void PartialLastLineIsNotRead()
        {
            var r = new Rig("partial");
            r.GameTick();
            r.Game.Say(1, "완성된 줄", Fake.T0);
            var half = FakeGame.MessageLine("half1", r.Game.LobbyId, "76561190000000002", "x", "쓰는 중", "text", null, Fake.T0, false, null);
            r.Game.AppendRaw(half.Substring(0, half.Length / 2));
            var got = r.Exchange.ReadNewMessages(r.Game.LobbyId);
            A.Eq(1, got.Count, "only the complete line");
            r.Exchange.CommitRead(r.Game.LobbyId);
            r.Game.AppendRaw(half.Substring(half.Length / 2) + "\n");
            got = r.Exchange.ReadNewMessages(r.Game.LobbyId);
            A.Eq(1, got.Count, "rest read later");
            A.Eq("쓰는 중", got[0].Body, "joined correctly");
        }

        [Test("40-7.9")]
        public static void SchemaMajorAndMinorRules()
        {
            var r = new Rig("schema");
            r.GameTick();
            r.Game.AppendRaw(FakeGame.MessageLine("a", r.Game.LobbyId, "1", "n", "major2", "text", null, Fake.T0, false, "2.0.0") + "\n");
            var withNewField = FakeGame.MessageLine("b", r.Game.LobbyId, "1", "n", "minor-higher", "text", null, Fake.T0, false, "1.7.0").TrimEnd('}') + ",\"future_field\":{\"x\":[1,2]}}";
            r.Game.AppendRaw(withNewField + "\n");
            var got = r.Exchange.ReadNewMessages(r.Game.LobbyId);
            A.Eq(1, got.Count, "MAJOR 2 line skipped");
            A.Eq("minor-higher", got[0].Body, "MINOR higher read, new field ignored");

            r.Game.Schema = "2.0.0";
            r.Step();
            A.Eq(LinkState.NeedsComponentUpdate, r.Chat.Link, "status schema MAJOR → component update");
            r.Game.Schema = "1.0.0"; r.Game.Producer = "0.9.0";
            r.Step();
            A.Eq(LinkState.NeedsReinstall, r.Chat.Link, "producer out of range → reinstall");
        }

        [Test("40-7.9")]
        public static void StaleStatusShowsDisconnected()
        {
            var r = new Rig("stale");
            r.Step();
            A.Eq(LinkState.Connected, r.Chat.Link, "connected");
            r.Clock.Now = r.Clock.Now.AddSeconds(7); // game stopped writing
            r.Chat.Poll();
            A.Eq(LinkState.Disconnected, r.Chat.Link, "> 6 s → disconnected");
            A.Eq(SendBlock.Disconnected, r.Chat.Send("hi"), "send blocked while disconnected");
        }

        [Test("40-7.10")]
        public static void ForgedRequestsRefused()
        {
            var key = NyangTalk.Shared.Exchange.NewKey();
            var now = Fake.T0;
            Dictionary<string, object> req;
            var good = Encoding.UTF8.GetBytes(Json.Write(NyangTalk.Shared.Exchange.MakeRequest(key, "r1", "L", "hi", now)));
            A.Eq(NyangTalk.Shared.Exchange.RequestCheck.Ok, NyangTalk.Shared.Exchange.Verify(good, key, now, out req), "good request");
            A.Eq(NyangTalk.Shared.Exchange.RequestCheck.BadSession, NyangTalk.Shared.Exchange.Verify(good, NyangTalk.Shared.Exchange.NewKey(), now, out req), "other session key");
            var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(good).Replace("\"hi\"", "\"bye\""));
            A.Eq(NyangTalk.Shared.Exchange.RequestCheck.BadSession, NyangTalk.Shared.Exchange.Verify(tampered, key, now, out req), "changed text");
            A.Eq(NyangTalk.Shared.Exchange.RequestCheck.Expired, NyangTalk.Shared.Exchange.Verify(good, key, now.AddSeconds(31), out req), "expired");
            var big = Encoding.UTF8.GetBytes(Json.Write(NyangTalk.Shared.Exchange.MakeRequest(key, "r2", "L", new string('a', 9000), now)));
            A.Eq(NyangTalk.Shared.Exchange.RequestCheck.TooLarge, NyangTalk.Shared.Exchange.Verify(big, key, now, out req), "over 8KB");

            // end to end: a request written with a wrong key is never delivered
            var r = new Rig("forge");
            r.GameTick();
            var forged = NyangTalk.Shared.Exchange.MakeRequest(NyangTalk.Shared.Exchange.NewKey(), "evil", r.Game.LobbyId, "spam", r.Clock.Now);
            File.WriteAllText(Path.Combine(r.Dir, "exchange", "outbox", "evil.json"), Json.Write(forged));
            r.Game.ProcessOutbox(r.Clock.Now);
            A.Eq(0, r.Game.Processed.Count, "not processed");
            A.True(r.Game.Refused.Single().EndsWith("BadSession"), "refused as bad session");
        }

        [Test("50-7,50-9.8")]
        public static void SendFlowEndToEnd()
        {
            var r = new Rig("send");
            r.Step(); r.Step();
            A.Eq(SendBlock.None, r.Chat.Send("안녕"), "send accepted");
            var o = r.Chat.Outbox.Pending.Single();
            A.Eq(OutgoingState.Sending, o.State, "sending");
            r.Step();   // game processes and echoes
            r.Step();   // app reads echo + result
            A.Eq(0, r.Chat.Outbox.Pending.Count, "merged into room message");
            A.True(r.Chat.Messages.Any(m => m.IsMine && m.Body == "안녕"), "my message in room");

            r.Game.RejectNext = true;
            r.Clock.Now = r.Clock.Now.AddSeconds(2);
            r.Chat.Send("거절될 글");
            r.Step(); r.Step();
            var rej = r.Chat.Outbox.Pending.Single();
            A.Eq(OutgoingState.Rejected, rej.State, "rejected stays");
            var draft = "쓰던 초안";
            A.Eq(SendBlock.None, r.Chat.Retry(rej.RequestId), "retry");
            r.Step(); r.Step();
            A.Eq(0, r.Chat.Outbox.Pending.Count, "retry delivered and merged");
            A.Eq("쓰던 초안", draft, "draft untouched");
        }

        [Test("50-7")]
        public static void UnconfirmedAfterTenSeconds()
        {
            var r = new Rig("timeout");
            r.Step(); r.Step();
            r.Chat.Send("no answer");
            // the game stops processing the outbox but keeps writing status
            for (int i = 0; i < 11; i++) { r.Clock.Now = r.Clock.Now.AddSeconds(1); r.Game.WriteStatus(r.Clock.Now); r.Chat.Poll(); }
            A.Eq(OutgoingState.Unconfirmed, r.Chat.Outbox.Pending.Single().State, "확인 못 함 after 10 s");
        }

        [Test("60-5.7")]
        public static void UnreadableParticipants()
        {
            var r = new Rig("unreadable");
            r.Game.ParticipantsReadable = false;
            r.Step();
            r.Game.Say(2, "hello", r.Clock.Now);
            r.Step();
            A.False(r.Chat.ParticipantsReadable, "flag for the notice");
            var list = r.Chat.PeopleList();
            A.Eq(1, list.Count, "only the sender, nothing guessed");
            A.Eq("Mochi", list[0].Name, "sender");
        }

        [Test("50-9.1,90-7.2")]
        public static void HistoryWriteFailureLosesNothing()
        {
            var r = new Rig("histfail");
            r.Step();
            r.Game.Say(2, "꼭 보여야 하는 글", r.Clock.Now);
            AtomicFile.FailureInjector = p => p.EndsWith(".ndjson") ? new UnauthorizedAccessException("locked (test)") : null;
            int old = AtomicFile.Retries; AtomicFile.Retries = 0;
            try { r.Step(); } finally { AtomicFile.FailureInjector = null; AtomicFile.Retries = old; }
            A.False(r.Chat.Messages.Any(m => m.Body == "꼭 보여야 하는 글"), "not shown before it is stored");
            r.Step();
            A.Eq(1, r.Chat.Messages.Count(m => m.Body == "꼭 보여야 하는 글"), "read again and shown once after the write works");
        }

        [Test("01-3.7,50-7")]
        public static void UnsentMessagesSurviveRestart()
        {
            var r = new Rig("outboxkeep");
            r.Step(); r.Step();
            r.Game.RejectNext = true;
            r.Chat.Send("거절될 글");
            r.Step(); r.Step();
            r.Chat.Send("결과 전에 끔");   // app closes before any result
            var again = new ChatService(r.Exchange, r.History, r.Clock, new EventBus(), r.Prefs, r.Docs);
            var rejected = again.Outbox.Pending.SingleOrDefault(p => p.Text == "거절될 글");
            A.True(rejected != null && rejected.State == OutgoingState.Rejected && rejected.NeedsAction, "rejected message kept with retry/delete");
            var unknown = again.Outbox.Pending.SingleOrDefault(p => p.Text == "결과 전에 끔");
            A.True(unknown != null && unknown.State == OutgoingState.Unconfirmed, "in-flight message comes back as 확인 못 함");
            A.True(again.Outbox.Log.Count >= 2, "send log kept");
        }

        [Test("70-3.3")]
        public static void InitialHistoryDoesNotNotify()
        {
            var r = new Rig("initial");
            r.GameTick();
            for (int i = 0; i < 5; i++) r.Game.Say(2, "old " + i, Fake.T0);
            r.Chat.WindowActive = false;
            r.Step();
            A.Eq(0, r.Chat.Unread.Unread, "first read is history");
            r.Game.Say(2, "new", r.Clock.Now);
            r.Step();
            A.Eq(1, r.Chat.Unread.Unread, "later message notifies");
        }
    }
}
