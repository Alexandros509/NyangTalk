using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NyangTalk.Shared;

namespace NyangTalk.App.Demo
{
    /// <summary>
    /// Fake game component for tests and the --demo mode: writes the exchange files the real plugin would write and
    /// answers send requests. Contains no game code or game data; names and texts are invented.
    /// </summary>
    public sealed class FakeGame
    {
        public const string ProducerVersion = "0.1.4";
        private readonly string _dir;
        private readonly Random _rnd = new Random(7);
        private int _seq;
        public string LobbyId = "109775240000123456";
        public string LobbyName = "데모 로비";
        public string LobbyCode = "DEMO42";
        public bool CodeHidden;
        public int CharLimit = 100;
        public bool CanSend = true;
        public bool RejectNext;
        /// <summary>null: passes the game's chat filter; "filter"/"punctuation": blocked (no echo, result "filtered").</summary>
        public string FilterResult;
        public string GameSession = "fake-session";
        public string Schema = Exchange.Schema;
        public string Producer = ProducerVersion;
        public readonly Dictionary<string, bool> Areas = new Dictionary<string, bool>
        {
            { "ChatReceive", true }, { "ChatSend", true }, { "LobbyInfo", true }, { "Participants", true }, { "Appearance", true }, { "Chest", true }
        };
        public readonly List<string[]> Members = new List<string[]>
        {
            new[] { "76561190000000001", "나", "me" },
            new[] { "76561190000000002", "토끼발", "owner" },
            new[] { "76561190000000003", "Mochi", "" },
            new[] { "76561190000000004", "ねこまる", "" }
        };
        public bool ParticipantsReadable = true;
        public readonly List<string> Processed = new List<string>();
        public readonly List<string> Refused = new List<string>();

        public FakeGame(string exchangeDir) { _dir = exchangeDir; Directory.CreateDirectory(_dir); }

        public string MyId { get { return Members[0][0]; } }

        public void WriteStatus(DateTime nowUtc)
        {
            var d = Exchange.Header(Producer);
            d["schema"] = Schema;
            d["written_utc"] = Exchange.Iso(nowUtc);
            d["ready"] = true;
            d["my_steam_id"] = MyId;
            var lobby = new Dictionary<string, object> { { "id", LobbyId }, { "name", LobbyName }, { "code", LobbyCode }, { "code_hidden", CodeHidden }, { "member_count", (long)Members.Count } };
            d["lobby"] = LobbyId == null ? null : lobby;
            d["can_send"] = CanSend;
            d["char_limit"] = (long)CharLimit;
            d["error"] = null;
            var areas = new Dictionary<string, object>();
            foreach (var kv in Areas) areas[kv.Key] = kv.Value;
            d["areas"] = areas;
            d["game_session"] = GameSession;
            if (ReportEvents) d["chat_events"] = ChatEvents;
            Write(Exchange.StatusFile, Json.Write(d));
        }

        /// <summary>Chat events "received by the hook" (95-SPEC 2.4); <see cref="DropLines"/> counts them without writing the line.</summary>
        public long ChatEvents;
        public bool ReportEvents = true, DropLines;

        public void WriteParticipants()
        {
            var d = Exchange.Header(Producer);
            d["lobby_id"] = LobbyId;
            d["readable"] = ParticipantsReadable;
            var l = new List<object>();
            if (ParticipantsReadable)
                foreach (var m in Members)
                    l.Add(new Dictionary<string, object> { { "steam_id", m[0] }, { "name", m[1] }, { "owner", m[2] == "owner" }, { "me", m[2] == "me" } });
            d["members"] = l;
            Write(Exchange.ParticipantsFile, Json.Write(d));
        }

        private void Write(string name, string text)
        {
            var p = Path.Combine(_dir, name);
            NyangTalk.Adapters.Storage.AtomicFile.WriteAllText(p, text);   // same swap and retries as the app
        }

        public static string MessageLine(string id, string lobbyId, string senderId, string senderName, string body, string kind, string emoteId, DateTime utc, bool mine, string schema)
        {
            var d = Exchange.Header(ProducerVersion);
            d["schema"] = schema ?? Exchange.Schema;
            d["id"] = id; d["lobby_id"] = lobbyId; d["sender_id"] = senderId; d["sender_name"] = senderName; d["body"] = body;
            d["kind"] = kind; d["emote_id"] = emoteId; d["time_utc"] = Exchange.Iso(utc); d["mine"] = mine; d["game_session"] = "fake-session";
            return Json.Write(d);
        }

        public void Say(int memberIndex, string body, DateTime utc, string kind = "text", string emoteId = null)
        {
            var m = Members[memberIndex];
            ChatEvents++;
            if (DropLines) return;
            AppendRaw(MessageLine("g" + LobbyId + "-" + (++_seq), LobbyId, m[0], m[1], body, kind, emoteId, utc, m[2] == "me", null) + "\n");
        }

        public void AppendRaw(string text)
        {
            var p = Path.Combine(_dir, Exchange.LobbyFile(LobbyId));
            using (var fs = new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                var b = new UTF8Encoding(false).GetBytes(text);
                fs.Write(b, 0, b.Length);
            }
        }

        /// <summary>Handles outbox requests the way the plugin must: verify, then deliver (echo) or refuse.</summary>
        public void ProcessOutbox(DateTime nowUtc)
        {
            var outbox = Path.Combine(_dir, Exchange.OutboxDir);
            if (!Directory.Exists(outbox)) return;
            var keyText = File.Exists(Path.Combine(_dir, Exchange.SessionKeyFile)) ? File.ReadAllText(Path.Combine(_dir, Exchange.SessionKeyFile)) : null;
            byte[] key = null;
            try { key = keyText == null ? null : Convert.FromBase64String(keyText.Trim()); } catch (FormatException) { }
            foreach (var f in Directory.GetFiles(outbox, "*.json"))
            {
                var raw = File.ReadAllBytes(f);
                File.Delete(f);
                Dictionary<string, object> req;
                var check = Exchange.Verify(raw, key, nowUtc, out req);
                if (check != Exchange.RequestCheck.Ok) { Refused.Add(Path.GetFileNameWithoutExtension(f) + ":" + check); continue; }
                var id = Json.Str(req, "request_id");
                var text = Json.Str(req, "text");
                Processed.Add(id);
                var res = Exchange.Header(Producer);
                res["request_id"] = id;
                if (RejectNext) { RejectNext = false; res["result"] = "rejected"; res["reason"] = "demo-reject"; }
                else if (FilterResult != null) { res["result"] = "filtered"; res["reason"] = FilterResult; }
                else
                {
                    res["result"] = "delivered";
                    Say(0, text, nowUtc);
                }
                Directory.CreateDirectory(Path.Combine(_dir, Exchange.ResultsDir));
                Write(Path.Combine(Exchange.ResultsDir, id + ".json"), Json.Write(res));
            }
        }

        private static readonly string[] DemoLines = { "안녕하세요!", "오늘 상자 뭐 나왔어요?", "Meow Meow", "야옹 야옹", "にゃんにゃ～ん", "ㅋㅋㅋ", "좋아요, 진짜예요.", "첫 번째 줄입니다.\n이 문장은 둘째 줄입니다.", "잠깐 자리 비울게요" };

        /// <summary>One random event for the demo loop.</summary>
        public void RandomChatter(DateTime nowUtc)
        {
            int who = 1 + _rnd.Next(Members.Count - 1);
            if (_rnd.Next(5) == 0) Say(who, "heart", nowUtc, "emote", "heart");
            else Say(who, DemoLines[_rnd.Next(DemoLines.Length)], nowUtc);
        }
    }
}
