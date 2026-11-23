using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.Storage
{
    public static class AppPaths
    {
        public static string Root()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NyangTalk");
        }
    }

    /// <summary>JSON documents under the app data folder; damaged files are kept as .broken-&lt;date&gt; (90-SPEC 2장).</summary>
    public sealed class JsonDocumentStore : IDocumentStore
    {
        private readonly string _dir;
        public JsonDocumentStore(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

        private string PathOf(string name) { return Path.Combine(_dir, name + ".json"); }

        public bool Exists(string name) { return File.Exists(PathOf(name)); }

        public bool HasBrokenCopy(string name) { return Directory.GetFiles(_dir, name + ".json.broken-*").Length > 0; }

        public IDictionary<string, object> Load(string name, out string brokenCopy)
        {
            brokenCopy = null;
            var path = PathOf(name);
            string text;
            try { text = AtomicFile.ReadAllText(path); }
            catch (IOException) { return null; }
            if (text == null) return null;
            object o;
            if (Json.TryParse(text, out o) && o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            var copy = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try { File.Move(path, copy); brokenCopy = copy; } catch (IOException) { brokenCopy = path; }
            return null;
        }

        public bool Save(string name, IDictionary<string, object> doc)
        {
            try { AtomicFile.WriteAllText(PathOf(name), Json.Write(doc)); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Conversation records: one NDJSON file per room plus a small index (90-SPEC 2·3장).</summary>
    public sealed class FileHistoryStore : IHistoryStore
    {
        private readonly string _dir;
        private readonly object _lock = new object();
        public const int FormatVersion = 1;

        public FileHistoryStore(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

        private string FileOf(string lobbyId) { return Path.Combine(_dir, Exchange.SafeId(lobbyId) + ".ndjson"); }
        private string IndexPath { get { return Path.Combine(_dir, "index.json"); } }

        public static string ToLine(ChatMessage m)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["v"] = (long)FormatVersion; d["t"] = "msg"; d["id"] = m.Id; d["sender_id"] = m.SenderId; d["sender_name"] = m.SenderName;
            d["body"] = m.Body; d["kind"] = m.Kind == MessageKind.Emote ? "emote" : (m.Kind == MessageKind.Other ? "other" : "text");
            d["emote_id"] = m.EmoteId; d["time_utc"] = Exchange.Iso(m.TimestampUtc); d["mine"] = m.IsMine;
            return Json.Write(d);
        }

        public static object FromLine(string line, string lobbyId)
        {
            object o;
            if (!Json.TryParse(line, out o)) return null;
            var d = Json.Obj(o);
            if (d == null) return null;
            DateTime t;
            if (!Exchange.TryIso(Json.Str(d, "time_utc"), out t)) return null;
            var type = Json.Str(d, "t");
            if (type != "msg") return null;
            var kind = Json.Str(d, "kind");
            return new ChatMessage
            {
                Id = Json.Str(d, "id"), LobbyId = lobbyId, SenderId = Json.Str(d, "sender_id"), SenderName = Json.Str(d, "sender_name"),
                Body = Json.Str(d, "body") ?? "", Kind = kind == "emote" ? MessageKind.Emote : (kind == "other" ? MessageKind.Other : MessageKind.Text),
                EmoteId = Json.Str(d, "emote_id"), TimestampUtc = t, IsMine = Json.Bool(d, "mine", false)
            };
        }

        public void Append(string lobbyId, string lobbyName, IList<ChatMessage> messages)
        {
            if (messages.Count == 0) return;
            lock (_lock)
            {
                AtomicFile.AppendLines(FileOf(lobbyId), messages.Select(ToLine));
                UpdateIndex(lobbyId, lobbyName, messages.Max(m => m.TimestampUtc), messages.Count);
            }
        }

        private Dictionary<string, object> LoadIndex()
        {
            var text = AtomicFile.ReadAllText(IndexPath);
            object o;
            if (text != null && Json.TryParse(text, out o) && o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private void UpdateIndex(string lobbyId, string name, DateTime last, int added)
        {
            var idx = LoadIndex();
            var e = Json.Obj(idx.ContainsKey(lobbyId) ? idx[lobbyId] : null) ?? new Dictionary<string, object>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(name)) e["name"] = name;
            e["last_utc"] = Exchange.Iso(last);
            e["count"] = Json.Long(e, "count", 0) + added;
            idx[lobbyId] = e;
            AtomicFile.WriteAllText(IndexPath, Json.Write(idx));
        }

        /// <summary>Number of bytes read by the last LoadTail (used by checks that the whole file is not read).</summary>
        public long LastTailBytesRead;

        public List<ChatMessage> LoadTail(string lobbyId, int countedWanted)
        {
            var result = new List<ChatMessage>();
            var path = FileOf(lobbyId);
            LastTailBytesRead = 0;
            if (!File.Exists(path)) return result;
            lock (_lock)
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long pos = fs.Length;
                    var pending = new List<byte>();
                    int counted = 0;
                    const int block = 64 * 1024;
                    var lines = new List<string>();
                    while (pos > 0 && counted < countedWanted)
                    {
                        int len = (int)Math.Min(block, pos);
                        pos -= len;
                        var buf = new byte[len];
                        fs.Seek(pos, SeekOrigin.Begin);
                        int read = 0;
                        while (read < len) { int n = fs.Read(buf, read, len - read); if (n <= 0) break; read += n; }
                        LastTailBytesRead += read;
                        // prepend buf to pending and cut complete lines from the end
                        var merged = new byte[len + pending.Count];
                        Buffer.BlockCopy(buf, 0, merged, 0, len);
                        pending.CopyTo(merged, len);
                        int end = merged.Length;
                        for (int i = merged.Length - 1; i >= 0; i--)
                        {
                            if (merged[i] != (byte)'\n') continue;
                            if (end - i - 1 > 0)
                            {
                                var line = Encoding.UTF8.GetString(merged, i + 1, end - i - 1);
                                if (AddParsed(line, lobbyId, result)) { if (HistoryWindow.Counts(result[result.Count - 1])) counted++; }
                                if (counted >= countedWanted) break;
                            }
                            end = i;
                        }
                        pending = new List<byte>(end);
                        for (int i = 0; i < end; i++) pending.Add(merged[i]);
                    }
                    if (counted < countedWanted && pos == 0 && pending.Count > 0)
                    {
                        var line = Encoding.UTF8.GetString(pending.ToArray());
                        AddParsed(line, lobbyId, result);
                    }
                }
            }
            result.Reverse();
            return result;
        }

        private static bool AddParsed(string line, string lobbyId, List<ChatMessage> msgs)
        {
            // lines of other kinds (e.g. join/leave lines written by earlier builds) are skipped
            var m = FromLine(line.TrimEnd((char)13), lobbyId) as ChatMessage;
            if (m == null) return false;
            msgs.Add(m);
            return true;
        }

        public List<RoomSummary> ListRooms()
        {
            var idx = LoadIndex();
            var r = new List<RoomSummary>();
            foreach (var kv in idx)
            {
                var e = Json.Obj(kv.Value);
                if (e == null || !File.Exists(FileOf(kv.Key))) continue;
                DateTime last;
                Exchange.TryIso(Json.Str(e, "last_utc"), out last);
                r.Add(new RoomSummary { LobbyId = kv.Key, Name = Json.Str(e, "name"), LastUtc = last, MessageCount = (int)Json.Long(e, "count", 0) });
            }
            return r.OrderByDescending(x => x.LastUtc).ToList();
        }

        public void DeleteRoom(string lobbyId)
        {
            lock (_lock)
            {
                var p = FileOf(lobbyId);
                if (File.Exists(p)) File.Delete(p);
                var idx = LoadIndex();
                if (idx.Remove(lobbyId)) AtomicFile.WriteAllText(IndexPath, Json.Write(idx));
            }
        }

        /// <summary>Removes records older than the retention period. Returns the number of removed lines.</summary>
        public int Cleanup(DateTime nowUtc, int retentionDays)
        {
            if (retentionDays <= 0) return 0;
            int removed = 0;
            lock (_lock)
            {
                var idx = LoadIndex();
                foreach (var file in Directory.GetFiles(_dir, "*.ndjson"))
                {
                    var lobbyId = Path.GetFileNameWithoutExtension(file);
                    var lines = File.ReadAllLines(file, Encoding.UTF8);
                    var keep = new List<string>();
                    int count = 0;
                    foreach (var l in lines)
                    {
                        object o = FromLine(l, lobbyId);
                        DateTime t = o is ChatMessage ? ((ChatMessage)o).TimestampUtc : nowUtc;
                        if (Domain.Preferences.RetentionPolicy.IsExpired(t, nowUtc, retentionDays)) { removed++; continue; }
                        keep.Add(l);
                        if (o is ChatMessage) count++;
                    }
                    if (keep.Count == lines.Length) continue;
                    if (keep.Count == 0) { File.Delete(file); idx.Remove(lobbyId); continue; }
                    AtomicFile.WriteAllText(file, string.Join("\n", keep) + "\n");
                    var e = Json.Obj(idx.ContainsKey(lobbyId) ? idx[lobbyId] : null);
                    if (e != null) e["count"] = (long)count;
                }
                AtomicFile.WriteAllText(IndexPath, Json.Write(idx));
            }
            return removed;
        }

        public List<SearchHit> SearchAll(string query, int max)
        {
            var hits = new List<SearchHit>();
            if (string.IsNullOrEmpty(query)) return hits;
            var q = TextNormalizer.Normalize(query, true, true);
            foreach (var room in ListRooms())
            {
                var path = FileOf(room.LobbyId);
                foreach (var l in File.ReadLines(path, Encoding.UTF8))
                {
                    var m = FromLine(l, room.LobbyId) as ChatMessage;
                    if (m == null) continue;
                    if (TextNormalizer.Normalize(m.Body, true, true).Contains(q) || TextNormalizer.Normalize(m.SenderName, true, true).Contains(q))
                    {
                        hits.Add(new SearchHit { LobbyId = room.LobbyId, RoomName = room.Name, Message = m });
                        if (hits.Count >= max) return hits;
                    }
                }
            }
            return hits;
        }
    }
}
