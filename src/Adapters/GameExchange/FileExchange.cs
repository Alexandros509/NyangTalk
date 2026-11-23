using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NyangTalk.Adapters.Storage;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.Sending;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.GameExchange
{
    /// <summary>File-based exchange channel (40-SPEC 4장).</summary>
    public sealed class FileExchange : IGameExchange
    {
        private readonly string _dir;
        private readonly Dictionary<string, long> _offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _uncommitted = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly byte[] _key;
        private readonly IDocumentStore _offsetStore;
        public DateTime? LastStatusFileTimeUtc;

        public FileExchange(string dir, IDocumentStore offsetStore)
        {
            _dir = dir;
            _offsetStore = offsetStore;
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, Exchange.OutboxDir));
            Directory.CreateDirectory(Path.Combine(dir, Exchange.ResultsDir));
            _key = Exchange.NewKey();
            // A new session key each app start: requests from an earlier run (or anyone without this key) are refused.
            AtomicFile.WriteAllText(Path.Combine(dir, Exchange.SessionKeyFile), Convert.ToBase64String(_key));
            LoadOffsets();
        }

        public string Folder { get { return _dir; } }
        public byte[] SessionKey { get { return _key; } }

        // last stored message per file (time and the ids at that time): after the component trims a file and the app has
        // to read it from the start again, everything up to here is skipped (40-SPEC 1.2: no duplicates)
        private readonly Dictionary<string, long> _lastTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _lastIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ChatMessage>> _batch = new Dictionary<string, List<ChatMessage>>(StringComparer.Ordinal);
        // the 64 bytes just before the stored offset: if they differ, the file was rewritten (trimmed) and is read again
        private readonly Dictionary<string, string> _tail = new Dictionary<string, string>(StringComparer.Ordinal);
        private const int TailBytes = 64;

        private static string TailAt(FileStream fs, long offset)
        {
            if (offset <= 0 || fs.Length < offset) return null;
            int n = (int)Math.Min(TailBytes, offset);
            var b = new byte[n];
            fs.Seek(offset - n, SeekOrigin.Begin);
            int read = 0;
            while (read < n) { int k = fs.Read(b, read, n - read); if (k <= 0) break; read += k; }
            return Convert.ToBase64String(b, 0, read);
        }

        private void LoadOffsets()
        {
            if (_offsetStore == null) return;
            string broken;
            var d = _offsetStore.Load("exchange-offsets", out broken);
            if (d == null) return;
            foreach (var kv in d)
            {
                if (kv.Value is long) _offsets[kv.Key] = (long)kv.Value;
                var o = kv.Value as IDictionary<string, object>;
                if (o == null) continue;
                object off, t, ids;
                if (o.TryGetValue("offset", out off) && off is long) _offsets[kv.Key] = (long)off;
                if (o.TryGetValue("last_ticks", out t) && t is long) _lastTicks[kv.Key] = (long)t;
                if (o.TryGetValue("last_ids", out ids) && ids is List<object>) _lastIds[kv.Key] = new HashSet<string>(((List<object>)ids).OfType<string>());
                object tail;
                if (o.TryGetValue("tail", out tail) && tail is string) _tail[kv.Key] = (string)tail;
            }
        }

        private void SaveOffsets()
        {
            if (_offsetStore == null) return;
            _offsetStore.Save("exchange-offsets", _offsets.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object>
            {
                { "offset", kv.Value },
                { "last_ticks", _lastTicks.ContainsKey(kv.Key) ? _lastTicks[kv.Key] : 0L },
                { "last_ids", _lastIds.ContainsKey(kv.Key) ? _lastIds[kv.Key].Cast<object>().ToList() : new List<object>() },
                { "tail", _tail.ContainsKey(kv.Key) ? _tail[kv.Key] : null }
            }));
        }

        /// <summary>Tells the game component how long to keep exchange lines (same period as the records).</summary>
        public void WriteRetention(int days)
        {
            AtomicFile.WriteAllText(Path.Combine(_dir, ExchangeRetention.SettingsFile), Json.Write(new Dictionary<string, object> { { "schema", Exchange.Schema }, { "retention_days", (long)days } }));
        }

        /// <summary>
        /// Trims expired lines from lobby files the app has fully read, only while the game is not writing (no status for
        /// 6 s). The component trims them itself while the game runs. Returns removed lines.
        /// </summary>
        public int TrimIdle(DateTime nowUtc, int days, bool gameActive)
        {
            if (days <= 0 || gameActive) return 0;
            int removed = 0;
            foreach (var path in Directory.GetFiles(_dir, "lobby-*.ndjson"))
            {
                var name = Path.GetFileName(path);
                long off; _offsets.TryGetValue(name, out off);
                if (new FileInfo(path).Length != off) continue; // not everything moved to the records yet
                removed += ExchangeRetention.TrimFile(path, nowUtc, days);
                _offsets[name] = new FileInfo(path).Length;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) _tail[name] = TailAt(fs, _offsets[name]);
            }
            SaveOffsets();
            return removed;
        }

        private Dictionary<string, object> ReadJson(string name)
        {
            string text;
            try { text = AtomicFile.ReadAllText(Path.Combine(_dir, name)); }
            catch (IOException) { return null; }
            object o;
            if (text == null || !Json.TryParse(text, out o)) return null;
            return Json.Obj(o);
        }

        public StatusSnapshot ReadStatus()
        {
            var d = ReadJson(Exchange.StatusFile);
            if (d == null) return null;
            var s = new StatusSnapshot
            {
                Schema = Json.Str(d, "schema"),
                ProducerVersion = Json.Str(d, "producer_version"),
                Ready = Json.Bool(d, "ready", false),
                CanSend = Json.Bool(d, "can_send", false),
                CharLimit = (int)Json.Long(d, "char_limit", 0),
                Error = Json.Str(d, "error"),
                MySteamId = Json.Str(d, "my_steam_id") == "0" ? null : Json.Str(d, "my_steam_id")
            };
            DateTime w;
            s.WrittenUtc = Exchange.TryIso(Json.Str(d, "written_utc"), out w) ? w : DateTime.MinValue;
            var lobby = Json.Obj(d.ContainsKey("lobby") ? d["lobby"] : null);
            if (lobby != null && Json.Str(lobby, "id") != null)
            {
                s.Lobby = new LobbyInfo
                {
                    LobbyId = Json.Str(lobby, "id"), Name = Json.Str(lobby, "name"), Code = Json.Str(lobby, "code"),
                    CodeHidden = Json.Bool(lobby, "code_hidden", false), MemberCount = (int)Json.Long(lobby, "member_count", 0)
                };
            }
            s.GameSession = Json.Str(d, "game_session");
            if (d.ContainsKey("chat_events") && d["chat_events"] != null) s.ChatEvents = Json.Long(d, "chat_events", 0);
            var emotes = Json.Obj(d.ContainsKey("emotes") ? d["emotes"] : null);
            if (emotes != null)
                foreach (var kv in emotes)
                {
                    var e = Json.Obj(kv.Value);
                    if (e == null) continue;
                    var f = Json.Str(e, "file");
                    s.Emotes[kv.Key] = new EmoteInfo { File = f == null ? null : Path.Combine(_dir, f.Replace('/', '\\')), Name = Json.Str(e, "name"), GivenUp = Json.Bool(e, "given_up", false) };
                }
            var chest = Json.Obj(d.ContainsKey("chest") ? d["chest"] : null);
            if (chest != null) { s.ChestState = Json.Str(chest, "state"); s.ChestReason = Json.Str(chest, "reason"); }
            var areas = Json.Obj(d.ContainsKey("areas") ? d["areas"] : null);
            if (areas != null) foreach (var kv in areas) if (kv.Value is bool) s.Areas[kv.Key] = (bool)kv.Value;
            return s;
        }

        public List<ChatMessage> ReadNewMessages(string lobbyId)
        {
            var name = Exchange.LobbyFile(lobbyId);
            var path = Path.Combine(_dir, name);
            var result = new List<ChatMessage>();
            if (!File.Exists(path)) return result;
            long offset;
            _offsets.TryGetValue(name, out offset);
            byte[] chunk;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                string tail;
                // file was trimmed or replaced (shorter, or different bytes before the stored position): start over,
                // skipping what was already stored
                if (fs.Length < offset || (offset > 0 && _tail.TryGetValue(name, out tail) && tail != null && TailAt(fs, offset) != tail)) offset = 0;
                if (fs.Length == offset) return result;
                fs.Seek(offset, SeekOrigin.Begin);
                chunk = new byte[fs.Length - offset];
                int read = 0;
                while (read < chunk.Length) { int n = fs.Read(chunk, read, chunk.Length - read); if (n <= 0) break; read += n; }
                if (read < chunk.Length) Array.Resize(ref chunk, read);
            }
            int lastNl = Array.LastIndexOf(chunk, (byte)'\n');
            if (lastNl < 0) return result; // only a line still being written: leave it for the next read
            var text = Encoding.UTF8.GetString(chunk, 0, lastNl + 1);
            _uncommitted[name] = offset + lastNl + 1;
            long lastTicks; _lastTicks.TryGetValue(name, out lastTicks);
            HashSet<string> lastIds; _lastIds.TryGetValue(name, out lastIds);
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                var m = ParseMessageLine(line.TrimEnd('\r'), lobbyId);
                if (m == null) continue;
                if (offset == 0 && lastTicks > 0 && (m.TimestampUtc.Ticks < lastTicks || (m.TimestampUtc.Ticks == lastTicks && lastIds != null && lastIds.Contains(m.Id)))) continue;
                result.Add(m);
            }
            _batch[name] = result;
            return result;
        }

        public void CommitRead(string lobbyId)
        {
            var name = Exchange.LobbyFile(lobbyId);
            long next;
            if (!_uncommitted.TryGetValue(name, out next)) return;
            _offsets[name] = next;
            _uncommitted.Remove(name);
            try
            {
                using (var fs = new FileStream(Path.Combine(_dir, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    _tail[name] = TailAt(fs, next);
            }
            catch (IOException) { _tail.Remove(name); }
            List<ChatMessage> batch;
            if (_batch.TryGetValue(name, out batch) && batch.Count > 0)
            {
                var max = batch.Max(m => m.TimestampUtc.Ticks);
                long prev; _lastTicks.TryGetValue(name, out prev);
                if (max > prev) { _lastTicks[name] = max; _lastIds[name] = new HashSet<string>(); }
                if (max >= prev) foreach (var m in batch.Where(m => m.TimestampUtc.Ticks == max)) _lastIds[name].Add(m.Id);
            }
            _batch.Remove(name);
            SaveOffsets();
        }

        public static ChatMessage ParseMessageLine(string line, string lobbyId)
        {
            object o;
            if (!Json.TryParse(line, out o)) return null;
            var d = Json.Obj(o);
            if (d == null) return null;
            var schema = Json.Str(d, "schema");
            if (schema == null || !schema.StartsWith("1.", StringComparison.Ordinal)) return null; // unknown MAJOR: not read
            DateTime t;
            if (!Exchange.TryIso(Json.Str(d, "time_utc"), out t)) return null;
            var kind = Json.Str(d, "kind");
            return new ChatMessage
            {
                Id = Json.Str(d, "id"), LobbyId = Json.Str(d, "lobby_id") ?? lobbyId, SenderId = Json.Str(d, "sender_id"),
                SenderName = Json.Str(d, "sender_name"), Body = Json.Str(d, "body") ?? "",
                Kind = kind == "emote" ? MessageKind.Emote : (kind == "text" || kind == null ? MessageKind.Text : MessageKind.Other),
                EmoteId = Json.Str(d, "emote_id"), TimestampUtc = t, IsMine = Json.Bool(d, "mine", false), GameSession = Json.Str(d, "game_session")
            };
        }

        public ParticipantsSnapshot ReadParticipants()
        {
            var d = ReadJson(Exchange.ParticipantsFile);
            if (d == null) return null;
            var s = new ParticipantsSnapshot { LobbyId = Json.Str(d, "lobby_id"), Readable = Json.Bool(d, "readable", false) };
            var l = Json.List(d, "members");
            if (l != null)
                foreach (var o in l)
                {
                    var m = Json.Obj(o);
                    if (m == null) continue;
                    s.Members.Add(new Participant { SteamId = Json.Str(m, "steam_id"), Name = Json.Str(m, "name"), IsOwner = Json.Bool(m, "owner", false), IsMe = Json.Bool(m, "me", false) });
                }
            return s;
        }

        public List<AppearanceRow> ReadAppearance()
        {
            var r = new List<AppearanceRow>();
            var d = ReadJson(Exchange.AppearanceFile);
            var l = Json.List(d, "members");
            if (l == null) return r;
            foreach (var o in l)
            {
                var m = Json.Obj(o);
                if (m == null) continue;
                var p = Json.Str(m, "portrait");
                r.Add(new AppearanceRow { SteamId = Json.Str(m, "steam_id"), Skin = Json.Str(m, "skin"), Hat = Json.Str(m, "hat"), PortraitFile = p == null ? null : Path.Combine(_dir, p.Replace('/', '\\')) });
            }
            return r;
        }

        public void WriteSendRequest(string requestId, string lobbyId, string text, DateTime nowUtc)
        {
            var req = Exchange.MakeRequest(_key, requestId, lobbyId, text, nowUtc);
            AtomicFile.WriteAllText(Path.Combine(_dir, Exchange.OutboxDir, requestId + ".json"), Json.Write(req));
        }

        public List<SendResultRow> ReadResults()
        {
            var r = new List<SendResultRow>();
            var dir = Path.Combine(_dir, Exchange.ResultsDir);
            if (!Directory.Exists(dir)) return r;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                string text;
                try { text = AtomicFile.ReadAllText(f); } catch (IOException) { continue; }
                object o;
                if (text == null || !Json.TryParse(text, out o)) continue;
                var d = Json.Obj(o);
                var kind = Json.Str(d, "result");
                // anything not clearly delivered/rejected is "확인 못 함", never shown as a firm result
                SendResultKind k = kind == "delivered" ? SendResultKind.Delivered : (kind == "rejected" ? SendResultKind.Rejected : (kind == "filtered" ? SendResultKind.Filtered : SendResultKind.Timeout));
                r.Add(new SendResultRow { RequestId = Json.Str(d, "request_id"), Kind = k, Reason = Json.Str(d, "reason") });
                try { File.Delete(f); } catch (IOException) { }
            }
            return r;
        }

        private long _chestLogOffset = -1;

        /// <summary>New lines of the game component's chest log since the last call (first call skips old lines).</summary>
        public List<ChestLogRow> ReadChestLog()
        {
            var r = new List<ChestLogRow>();
            var p = Path.Combine(_dir, Exchange.ChestLogFile);
            if (!File.Exists(p)) { _chestLogOffset = 0; return r; }
            using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (_chestLogOffset < 0 || fs.Length < _chestLogOffset) { _chestLogOffset = fs.Length; return r; }
                if (fs.Length == _chestLogOffset) return r;
                fs.Seek(_chestLogOffset, SeekOrigin.Begin);
                var buf = new byte[fs.Length - _chestLogOffset];
                int read = fs.Read(buf, 0, buf.Length);
                int last = Array.LastIndexOf(buf, (byte)'\n', read - 1);
                if (last < 0) return r;
                _chestLogOffset += last + 1;
                foreach (var line in Encoding.UTF8.GetString(buf, 0, last + 1).Split('\n'))
                {
                    object o;
                    if (line.Length == 0 || !Json.TryParse(line, out o)) continue;
                    var d = Json.Obj(o);
                    DateTime t;
                    Exchange.TryIso(Json.Str(d, "time_utc"), out t);
                    r.Add(new ChestLogRow { TimeUtc = t, Kind = Json.Str(d, "kind"), Member = Json.Str(d, "member"), Called = Json.Bool(d, "called", false), Reason = Json.Str(d, "reason") });
                }
            }
            return r;
        }

        public void WritePermit(IDictionary<string, object> permit, DateTime nowUtc)
        {
            var d = new Dictionary<string, object>(permit, StringComparer.Ordinal);
            d["schema"] = Exchange.Schema;
            d["written_utc"] = Exchange.Iso(nowUtc);
            AtomicFile.WriteAllText(Path.Combine(_dir, Exchange.PermitFile), Json.Write(d));
        }
    }
}
