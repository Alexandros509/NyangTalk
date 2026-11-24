using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using NyangTalk.Shared;
using UnityEngine;

namespace NyangTalk.GameLink
{
    /// <summary>
    /// NyangTalk game component (BepInEx 5 plugin, connection method A, 40-SPEC 2장).
    /// Reads chat, lobby, participants and looks; forwards send requests from the app. It does not change the game
    /// screen or game state except sending requested chat and opening chests under the app's fresh, session-bound permit.
    /// Every callback is isolated from exceptions.
    /// </summary>
    [BepInPlugin("local.nyangtalk.gamelink", "NyangTalk GameLink", Version)]
    public sealed partial class GameLinkPlugin : BaseUnityPlugin
    {
        public const string Version = "0.1.4";
        private ContractRuntime _c;
        private string _dir;
        private float _next;
        private bool _hooked;
        private string _lobbyId;
        private int _seq;
        private long _chatEvents;
        private readonly Dictionary<ulong, string> _names = new Dictionary<ulong, string>();
        private readonly HashSet<string> _currentMembers = new HashSet<string>();
        private readonly ComponentErrors _errors = new ComponentErrors();

        private void Awake()
        {
            try
            {
                _dir = Exchange.DefaultFolder();
                var here = Path.GetDirectoryName(Info.Location);
                var contract = ContractFile.Parse(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(here, "contract.json"))));
                _c = new ContractRuntime(contract);
                PrepareCatalog();
                Directory.CreateDirectory(_dir);
                Directory.CreateDirectory(Path.Combine(_dir, Exchange.OutboxDir));
                Directory.CreateDirectory(Path.Combine(_dir, Exchange.ResultsDir));
                Logger.LogInfo("NyangTalk GameLink " + Version + " ready; exchange: " + _dir);
            }
            catch (Exception ex) { _errors.Set("init", ex.GetType().Name); Logger.LogError(ex); }
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            if (_c == null)
            {
                // start failed: still report it so the app does not wait forever
                Safe("status", () => { if (_dir != null) { var d = Exchange.Header(Version); d["written_utc"] = Exchange.Iso(DateTime.UtcNow); d["ready"] = false; d["lobby"] = null; d["can_send"] = false; d["error"] = _errors.Error; d["areas"] = new Dictionary<string, object>(); Directory.CreateDirectory(_dir); WriteAtomic(Exchange.StatusFile, Json.Write(d)); } });
                return;
            }
            Safe("resolve", () => { if (_c.Areas.Count == 0 || !_hooked) _c.ResolveAll(); });
            Safe("hook", Hook);
            Safe("trim", TrimDaily);
            Safe("lobby", WriteLobby);
            Safe("outbox", ProcessOutbox);
            Safe("chest", ChestTick);
            Safe("status", WriteStatus);
        }

        private DateTime _nextTrim = DateTime.MinValue;

        /// <summary>40-SPEC 1.2: keeps writing chat even when the app is closed, and removes lines older than the user's period.</summary>
        private void TrimDaily()
        {
            if (DateTime.UtcNow < _nextTrim) return;
            _nextTrim = DateTime.UtcNow.AddDays(1);
            int days = ExchangeRetention.ReadDays(_dir, 30);
            foreach (var f in Directory.GetFiles(_dir, "lobby-*.ndjson")) ExchangeRetention.TrimFile(f, DateTime.UtcNow, days);
        }

        private void Safe(string what, Action a)
        {
            _errors.Run(what, a, ex => Logger.LogWarning(what + " failed: " + ex.Message));
        }

        // ---------------------------------------------------------------- receive

        private void Hook()
        {
            if (_hooked || !_c.Areas.ContainsKey("ChatReceive") || !_c.Areas["ChatReceive"]) return;
            var mp = _c.Get("mp.instance", null);
            if (mp == null) return; // multiplayer object not created yet
            var idType = _c.TypeOf("steamid.value");
            var chatBus = _c.Get("mp.chatBus", mp);
            var list = _c.Get("bus.global", chatBus) as IList;
            if (list == null) return;
            list.Add(ContractRuntime.MakeAction(this, "OnChat", idType, typeof(string)));
            var emoteBus = _c.Has("mp.emoteBus") ? _c.Get("mp.emoteBus", mp) : null;
            var elist = emoteBus == null ? null : _c.Get("bus.global", emoteBus) as IList;
            if (elist != null) elist.Add(ContractRuntime.MakeAction(this, "OnEmote", idType, typeof(int)));
            _hooked = true;
            Logger.LogInfo("chat hooks registered");
        }

        private ulong IdOf(object steamId)
        {
            var v = steamId == null ? null : _c.Get("steamid.value", steamId);
            return v is ulong ? (ulong)v : 0UL;
        }

        private ulong MyId()
        {
            var me = _c.Get("user.me", null);
            var v = me == null ? null : _c.Get("user.steamId", me);
            return v is ulong ? (ulong)v : 0UL;
        }

        // Called by the game's bus on the main thread. Must never throw into the game.
        private void OnChat<TId>(TId sender, string text)
        {
            Safe("chat-write", () => Record(IdOf(sender), text ?? "", "text", null));
        }

        private void OnEmote<TId>(TId sender, int emote)
        {
            Safe("emote-write", () => { Record(IdOf(sender), "emote:" + emote, "emote", emote.ToString()); RequestEmotePicture(emote); });
        }

        private void Record(ulong sender, string body, string kind, string emoteId)
        {
            if (_lobbyId == null) return;
            _chatEvents++;   // reported in status as chat_events (95-SPEC 2.4), counted before the write so a failed write shows
            ulong me = MyId();
            // "mine" only when my own Steam ID is known and equals the sender (50-SPEC 7장: no guessing)
            bool mine = sender != 0 && me != 0 && sender == me;
            string name;
            if (!_names.TryGetValue(sender, out name)) name = sender.ToString();
            AppendMessage(sender, name, body, kind, emoteId, mine);
        }

        private void AppendMessage(ulong sender, string name, string body, string kind, string emoteId, bool mine)
        {
            var d = Exchange.Header(Version);
            d["id"] = _lobbyId + "-" + DateTime.UtcNow.Ticks + "-" + (++_seq);
            d["lobby_id"] = _lobbyId;
            d["sender_id"] = sender.ToString();
            d["sender_name"] = name;
            d["body"] = body;
            d["kind"] = kind;
            d["emote_id"] = emoteId;
            d["time_utc"] = Exchange.Iso(DateTime.UtcNow);
            d["mine"] = mine;
            d["game_session"] = _gameSession;
            var line = Json.Write(d) + "\n";
            var path = Path.Combine(_dir, Exchange.LobbyFile(_lobbyId));
            var bytes = new UTF8Encoding(false).GetBytes(line);
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(); }
        }

        // ---------------------------------------------------------------- lobby, participants, looks

        private object CurrentLobby()
        {
            var lobby = _c.Get("lobby.current", null);
            if (lobby == null) return null;
            var valid = _c.Get("lobby.isValid", lobby);
            return valid is bool && (bool)valid ? lobby : null;
        }

        private void WriteLobby()
        {
            var lobby = CurrentLobby();
            if (lobby == null) { _lobbyId = null; return; }
            _lobbyId = IdOf(_c.Get("lobby.steamId", lobby)).ToString();
            var members = _c.Get("lobby.members", lobby) as Array;
            var list = new List<object>();
            var looks = new List<object>();
            _currentMembers.Clear();
            bool readable = members != null && _c.Areas.ContainsKey("Participants") && _c.Areas["Participants"];
            if (readable)
            {
                foreach (var m in members)
                {
                    var user = _c.Get("member.user", m);
                    var idObj = user == null ? null : _c.Get("user.steamId", user);
                    ulong id = idObj is ulong ? (ulong)idObj : 0UL;
                    var name = user == null ? null : _c.Get("user.name", user) as string;
                    var isMe = user == null ? null : _c.Get("user.isMe", user);
                    var owner = _c.Get("member.isOwner", m);
                    if (id != 0 && name != null) _names[id] = name;
                    if (id != 0) _currentMembers.Add(id.ToString());
                    list.Add(new Dictionary<string, object> { { "steam_id", id.ToString() }, { "name", name }, { "owner", owner is bool && (bool)owner }, { "me", isMe is bool && (bool)isMe } });
                    if (_c.Areas.ContainsKey("Appearance") && _c.Areas["Appearance"])
                    {
                        var skin = _c.Get("member.skin", m); var hat = _c.Get("member.hat", m);
                        string portrait = null;
                        if (skin is int && hat is int) { try { portrait = PortraitFor(id.ToString(), (int)skin, (int)hat); } catch (Exception ex) { Logger.LogWarning("portrait: " + ex.Message); } }
                        looks.Add(new Dictionary<string, object> { { "steam_id", id.ToString() }, { "skin", skin == null ? null : skin.ToString() }, { "hat", hat == null ? null : hat.ToString() }, { "portrait", portrait } });
                    }
                }
            }
            var p = Exchange.Header(Version);
            p["lobby_id"] = _lobbyId; p["readable"] = readable; p["members"] = list;
            WriteAtomic(Exchange.ParticipantsFile, Json.Write(p));
            var a = Exchange.Header(Version);
            a["lobby_id"] = _lobbyId; a["members"] = looks;
            WriteAtomic(Exchange.AppearanceFile, Json.Write(a));
        }

        /// <summary>The code exactly as the game shows it, unless the owner hid it (40-SPEC 2장).</summary>
        private void LobbyCode(object lobby, out string code, out bool hidden)
        {
            code = null; hidden = false;
            var hideKey = _c.Get("lobbyIdDisplay.hideKey", null) as string;
            if (hideKey != null && _c.Has("lobby.data"))
            {
                var v = _c.Call("lobby.data", lobby, hideKey) as string;
                hidden = !string.IsNullOrEmpty(v) && v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
            }
            var displayType = _c.TypeOf("lobbyIdDisplay.type");
            if (displayType == null || !_c.Has("lobbyIdDisplay.codeText") || !_c.Has("tmp.text")) return;
            foreach (var o in Resources.FindObjectsOfTypeAll(displayType))
            {
                if (!InLoadedScene(o)) continue; // skip prefabs/assets: only what is on screen
                var txt = _c.Get("lobbyIdDisplay.codeText", o);
                var s = txt == null ? null : _c.Get("tmp.text", txt) as string;
                if (!string.IsNullOrEmpty(s)) { code = s.Trim(); return; }
            }
        }

        private static bool InLoadedScene(UnityEngine.Object o)
        {
            var c = o as Component;
            return c != null && c.gameObject.scene.isLoaded;
        }

        private Component MainSpeech()
        {
            var t = _c.TypeOf("speech.type");
            if (t == null || !_c.Has("speech.isMain")) return null;
            foreach (var o in Resources.FindObjectsOfTypeAll(t))
            {
                if (!InLoadedScene(o)) continue;
                var main = _c.Get("speech.isMain", o);
                var component = (Component)o;
                if (main is bool && (bool)main && component.gameObject.activeInHierarchy) return component;
            }
            return null;
        }

        private int CharLimit()
        {
            var speech = MainSpeech();
            if (speech == null || !_c.Has("speech.maxChars")) return 0;
            var value = _c.Get("speech.maxChars", speech);
            if (value is int && (int)value > 0) return (int)value;
            return 0;
        }

        private void WriteStatus()
        {
            var d = Exchange.Header(Version);
            d["written_utc"] = Exchange.Iso(DateTime.UtcNow);
            d["ready"] = _c.Areas.Count > 0;
            var lobby = CurrentLobby();
            if (lobby != null)
            {
                string code; bool hidden;
                LobbyCode(lobby, out code, out hidden);
                d["lobby"] = new Dictionary<string, object>
                {
                    { "id", _lobbyId }, { "name", _c.Get("lobby.name", lobby) as string }, { "code", code }, { "code_hidden", hidden },
                    { "member_count", Convert.ToInt64(_c.Get("lobby.memberCount", lobby) ?? 0) }
                };
            }
            else d["lobby"] = null;
            bool sendOk = _c.Areas.ContainsKey("ChatSend") && _c.Areas["ChatSend"];
            d["can_send"] = sendOk && lobby != null && MainSpeech() != null;
            d["char_limit"] = (long)CharLimit();
            var me = MyId();
            d["my_steam_id"] = me == 0 ? null : me.ToString();
            d["hooked"] = _hooked;
            d["chat_events"] = _chatEvents;
            d["error"] = _errors.Error;
            var areas = new Dictionary<string, object>();
            foreach (var kv in _c.Areas) areas[kv.Key] = kv.Value;
            if (!_hooked) areas["ChatReceive"] = false; // resolved but not yet listening: not "working"
            d["areas"] = areas;
            d["game_session"] = _gameSession;
            d["emotes"] = EmoteStatus();
            d["chest"] = new Dictionary<string, object> { { "state", _chest.State }, { "reason", _chest.Reason } };
            WriteAtomic(Exchange.StatusFile, Json.Write(d));
        }

        // ---------------------------------------------------------------- send

        private byte[] SessionKey()
        {
            var p = Path.Combine(_dir, Exchange.SessionKeyFile);
            if (!File.Exists(p)) return null;
            try { return Convert.FromBase64String(Encoding.UTF8.GetString(ExchangeFiles.ReadAllBytes(p)).Trim()); } catch (FormatException) { return null; }
        }

        private void ProcessOutbox()
        {
            var dir = Path.Combine(_dir, Exchange.OutboxDir);
            var files = Directory.GetFiles(dir, "*.json");
            if (files.Length == 0) return;
            var key = SessionKey();
            foreach (var f in files)
            {
                byte[] raw;
                try { raw = File.ReadAllBytes(f); File.Delete(f); } catch (IOException) { continue; }
                Dictionary<string, object> req;
                var check = Exchange.Verify(raw, key, DateTime.UtcNow, out req);
                if (check != Exchange.RequestCheck.Ok) { Logger.LogWarning("refused request " + Path.GetFileName(f) + ": " + check); continue; }
                var id = Json.Str(req, "request_id");
                var text = Json.Str(req, "text");
                string result = "delivered", reason = null;
                int limit = CharLimit();
                if (_lobbyId == null || Json.Str(req, "lobby_id") != _lobbyId) { result = "rejected"; reason = "not-in-lobby"; }
                else if (limit > 0 && text.Length > limit) { result = "rejected"; reason = "too-long"; }
                else if (!_c.Has("speech.send")) { result = "rejected"; reason = "send-unsupported"; }
                else
                {
                    try
                    {
                        var speech = MainSpeech();
                        if (speech == null) { result = "rejected"; reason = "speech-not-ready"; }
                        else
                        {
                            // Use the game's complete local path: filter, network send and my own cat's bubble.
                            bool punctuation = false;
                            bool? blocked = null;
                            try { blocked = FilterBlocks(text, out punctuation); } catch (Exception fex) { Logger.LogWarning("filter check: " + fex.Message); }
                            if (!FilteredSend.Run(blocked, () => _c.Call("speech.send", speech, text)))
                            { result = "filtered"; reason = punctuation ? "punctuation" : "filter"; }
                        }
                    }
                    catch (Exception ex) { result = "rejected"; reason = ex.GetType().Name; }
                }
                var r = Exchange.Header(Version);
                r["request_id"] = id; r["result"] = result; r["reason"] = reason;
                WriteAtomic(Path.Combine(Exchange.ResultsDir, id + ".json"), Json.Write(r));
            }
        }

        /// <summary>
        /// Writes a whole file in one call. The game's stripped mscorlib has no File.Move/File.Replace, so this is not an
        /// atomic swap; the app treats an unreadable (half written) file as "not readable right now" and retries.
        /// </summary>
        private void WriteAtomic(string name, string text)
        {
            var path = Path.Combine(_dir, name);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { fs.Write(bytes, 0, bytes.Length); fs.Flush(); }
        }
    }
}
