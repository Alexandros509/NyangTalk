using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Application
{
    /// <summary>
    /// Use cases around the current room: receive messages, track people, send, open saved rooms.
    /// Driven by <see cref="Poll"/> on a timer (0.75–1 s, 90-SPEC 6장).
    /// </summary>
    public sealed class ChatService
    {
        private readonly IGameExchange _exchange;
        private readonly IHistoryStore _history;
        private readonly IClock _clock;
        private readonly EventBus _bus;
        private readonly PreferencesService _prefs;

        public readonly Outbox Outbox = new Outbox();
        public readonly UnreadTracker Unread = new UnreadTracker();
        public readonly HistoryWindow Window = new HistoryWindow();
        public readonly AreaAvailability Areas = new AreaAvailability();

        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        private int _countedAvailable;
        private bool _firstPollForLobby = true;

        public StatusSnapshot Status { get; private set; }
        public LinkState Link { get; private set; }
        public ParticipantsSnapshot Participants { get; private set; }
        public string CurrentLobbyId { get; private set; }
        /// <summary>Non-null while a saved (archived) room is open read-only.</summary>
        public string ArchivedLobbyId { get; private set; }
        public string ArchivedName { get; private set; }
        public bool Installed = true;
        public bool WindowActive = true;
        public bool AtBottom = true;

        private readonly IDocumentStore _docs;
        public const string OutboxDoc = "outbox";
        public const string RoomGroupsDoc = "room-groups";
        public readonly RoomGroupBook RoomGroups = new RoomGroupBook();
        /// <summary>When the participant list was last confirmed while connected (95-SPEC 2.8).</summary>
        public DateTime? LastParticipantsUtc { get; private set; }
        private List<Participant> _lastPeople = new List<Participant>();

        public ChatService(IGameExchange exchange, IHistoryStore history, IClock clock, EventBus bus, PreferencesService prefs, IDocumentStore docs = null)
        {
            _exchange = exchange; _history = history; _clock = clock; _bus = bus; _prefs = prefs; _docs = docs;
            Link = LinkState.Waiting;
            LoadOutbox();
            LoadRoomGroups();
            _bus.Subscribe<OutboxChanged>(e => SaveOutbox());
        }

        private void LoadRoomGroups()
        {
            if (_docs == null) return;
            string broken;
            var d = _docs.Load(RoomGroupsDoc, out broken);
            if (d == null) return;
            object v;
            if (!d.TryGetValue("groups", out v) || !(v is System.Collections.IList)) return;
            var list = new List<RoomGroup>();
            foreach (var o in (System.Collections.IList)v)
            {
                var m = o as IDictionary<string, object>;
                if (m == null) continue;
                object name, ids;
                m.TryGetValue("name", out name); m.TryGetValue("lobbies", out ids);
                if (!(ids is System.Collections.IList)) continue;
                var g = new RoomGroup { Name = name as string };
                foreach (var x in (System.Collections.IList)ids) if (x is string) g.LobbyIds.Add((string)x);
                list.Add(g);
            }
            RoomGroups.Load(list);
        }

        public bool SaveRoomGroups()
        {
            if (_docs == null) return true;
            var doc = new Dictionary<string, object>
            {
                { "format", "1.0.0" },
                { "groups", RoomGroups.All.Select(g => (object)new Dictionary<string, object> { { "name", g.Name }, { "lobbies", g.LobbyIds.Select(x => (object)x).ToList() } }).ToList() }
            };
            return _docs.Save(RoomGroupsDoc, doc);
        }

        /// <summary>95-SPEC 2.7: links the chosen saved rooms into one conversation group and reloads the view if open.</summary>
        public RoomGroup GroupRooms(IEnumerable<string> lobbyIds, string name = null)
        {
            var g = RoomGroups.Group(lobbyIds, name);
            if (g == null) return null;
            SaveRoomGroups();
            if (IsArchivedView && g.LobbyIds.Contains(ArchivedLobbyId, StringComparer.Ordinal))
            {
                if (!string.IsNullOrEmpty(g.Name)) ArchivedName = g.Name;
                LoadRoom(ArchivedLobbyId);
            }
            _bus.Publish(new LobbyChanged { LobbyId = ViewLobbyId });
            return g;
        }

        /// <summary>95-SPEC 2.7: dissolves the group containing <paramref name="lobbyId"/> without touching the room record files.</summary>
        public bool UngroupRooms(string lobbyId)
        {
            if (!RoomGroups.Ungroup(lobbyId)) return false;
            SaveRoomGroups();
            if (IsArchivedView) LoadRoom(ArchivedLobbyId);
            _bus.Publish(new LobbyChanged { LobbyId = ViewLobbyId });
            return true;
        }

        /// <summary>
        /// Unsent messages and the send log survive restarts (01-OVERVIEW 3장 7번, 50-SPEC 7장). A message that was
        /// "sending" when the app closed has no known result, so it comes back as "확인 못 함" with retry/delete.
        /// </summary>
        private void LoadOutbox()
        {
            if (_docs == null) return;
            string broken;
            var d = _docs.Load(OutboxDoc, out broken);
            if (d == null) return;
            Func<object, OutgoingMessage> read = o =>
            {
                var m = o as IDictionary<string, object>;
                if (m == null) return null;
                object id, text, state, created, reason;
                m.TryGetValue("id", out id); m.TryGetValue("text", out text); m.TryGetValue("state", out state); m.TryGetValue("created_ticks", out created); m.TryGetValue("reason", out reason);
                if (!(id is string) || !(text is string)) return null;
                OutgoingState st;
                if (!Enum.TryParse(state as string ?? "", out st)) st = OutgoingState.Unconfirmed;
                object lobby, session;
                m.TryGetValue("lobby", out lobby); m.TryGetValue("session", out session);
                return new OutgoingMessage { RequestId = (string)id, Text = (string)text, State = st, Reason = reason as string, LobbyId = lobby as string, GameSession = session as string, CreatedUtc = created is long ? new DateTime((long)created, DateTimeKind.Utc) : DateTime.MinValue };
            };
            object log, pending;
            if (d.TryGetValue("log", out log) && log is System.Collections.IList)
                Outbox.LoadLog(((System.Collections.IList)log).Cast<object>().Select(read).Where(x => x != null));
            if (d.TryGetValue("pending", out pending) && pending is System.Collections.IList)
                foreach (var m in ((System.Collections.IList)pending).Cast<object>().Select(read).Where(x => x != null))
                {
                    if (m.State == OutgoingState.Sending || m.State == OutgoingState.Delivered) m.State = OutgoingState.Unconfirmed;
                    Outbox.Pending.Add(m);
                }
        }

        public bool SaveOutbox()
        {
            if (_docs == null) return true;
            Func<OutgoingMessage, object> write = m => new Dictionary<string, object> { { "id", m.RequestId }, { "text", m.Text }, { "state", m.State.ToString() }, { "reason", m.Reason }, { "created_ticks", m.CreatedUtc.Ticks }, { "lobby", m.LobbyId }, { "session", m.GameSession } };
            var doc = new Dictionary<string, object>
            {
                { "format", "1.0.0" },
                { "pending", Outbox.Pending.Select(write).ToList() },
                { "log", Outbox.Log.Select(write).ToList() }
            };
            bool ok = _docs.Save(OutboxDoc, doc);
            if (!ok) _bus.Publish(new NoticeRaised { Key = "outbox-save", Text = "send.saveFailed" });
            return ok;
        }

        public IList<ChatMessage> Messages { get { return _messages; } }
        public bool IsArchivedView { get { return ArchivedLobbyId != null; } }
        public string ViewLobbyId { get { return ArchivedLobbyId ?? CurrentLobbyId; } }

        /// <summary>False when the last poll got no status (missing, locked or broken file) and kept the earlier one.</summary>
        public bool LastPollReadStatus { get; private set; }

        public void Poll()
        {
            var now = _clock.UtcNow;
            var st = _exchange.ReadStatus();
            LastPollReadStatus = st != null;
            if (st != null) Status = st;
            var verdict = Status == null ? ExchangeVerdict.Ok : ExchangeCompatibility.Check(Status.Schema, Status.ProducerVersion);
            var link = LinkHealth.Evaluate(Installed, Status == null ? (DateTime?)null : Status.WrittenUtc, verdict, now);
            if (link != Link) { Link = link; _bus.Publish(new LinkStateChanged { State = link }); }
            if (verdict != ExchangeVerdict.Ok || Status == null) { TickTimers(now); return; }

            Areas.ClearRuntime();
            foreach (var kv in Status.Areas)
            {
                FeatureArea a;
                if (Enum.TryParse(kv.Key, out a)) Areas.SetRuntime(a, kv.Value);
            }

            var lobbyId = Status.Lobby == null ? null : Status.Lobby.LobbyId;
            if (lobbyId != CurrentLobbyId)
            {
                if (ArchivedLobbyId == null)
                {
                    try { LoadRoom(lobbyId); }
                    catch (IOException) { _bus.Publish(new NoticeRaised { Key = "history-read", Text = "history.readFailed" }); TickTimers(now); return; }
                    catch (UnauthorizedAccessException) { _bus.Publish(new NoticeRaised { Key = "history-read", Text = "history.readFailed" }); TickTimers(now); return; }
                }
                CurrentLobbyId = lobbyId;   // only after the room loaded, so a failed load is retried next poll
                _firstPollForLobby = true;
                _bus.Publish(new LobbyChanged { LobbyId = lobbyId });
            }

            if (lobbyId != null)
            {
                var fresh = _exchange.ReadNewMessages(lobbyId).Where(m => m.Id != null && !_ids.Contains(m.Id)).ToList();
                bool initial = _firstPollForLobby;
                _firstPollForLobby = false;
                bool stored = true;
                if (fresh.Count > 0)
                {
                    try { _history.Append(lobbyId, Status.Lobby.Name, fresh); }
                    catch (IOException) { stored = false; }
                    catch (UnauthorizedAccessException) { stored = false; }
                    if (!stored)
                    {
                        // not consumed: the same lines are read again next poll, nothing is lost
                        _bus.Publish(new NoticeRaised { Key = "history-write", Text = "history.writeFailed" });
                        fresh.Clear();
                        _firstPollForLobby = initial;
                    }
                }
                if (stored) _exchange.CommitRead(lobbyId);
                if (fresh.Count > 0)
                {
                    foreach (var m in fresh)
                    {
                        _ids.Add(m.Id);
                        ReceivedCount++;
                        if (ArchivedLobbyId == null)
                        {
                            _messages.Add(m);
                            if (HistoryWindow.Counts(m)) _countedAvailable++;
                        }
                        if (Outbox.OnEcho(m, Status == null ? null : Status.MySteamId) != null) _bus.Publish(new OutboxChanged());
                        Unread.OnMessage(m, initial, WindowActive, AtBottom);
                        _bus.Publish(new MessageReceived { Message = m, IsInitialLoad = initial });
                    }
                    TrimToWindow();
                }

                var parts = _exchange.ReadParticipants();
                if (parts != null && parts.LobbyId == lobbyId)
                {
                    Participants = parts;
                    if (Link == LinkState.Connected)
                    {
                        LastParticipantsUtc = now;
                        var resolved = ResolveCurrentPeople();
                        if (resolved.Count > 0) _lastPeople = resolved;
                    }
                    _bus.Publish(new ParticipantsChanged());
                }
                else if (Link == LinkState.Connected)
                {
                    var resolved = ResolveCurrentPeople();
                    if (resolved.Count > 0) { LastParticipantsUtc = now; _lastPeople = resolved; }
                }
                foreach (var r in _exchange.ReadResults())
                {
                    Outbox.ApplyResult(r.RequestId, r.Kind, r.Reason);
                    _bus.Publish(new OutboxChanged());
                }
            }
            Receive.Observe(now, Link == LinkState.Connected && lobbyId != null, (Status.GameSession ?? "") + "|" + lobbyId, Status.ChatEvents, ReceivedCount);
            TickTimers(now);
        }

        /// <summary>Chat lines read from the game component since the app started (95-SPEC 2.4).</summary>
        public long ReceivedCount { get; private set; }
        public readonly ReceiveWatch Receive = new ReceiveWatch();

        /// <summary>"다시 연결": starts the health judgement over and reads the game component's files again now.</summary>
        public void Reconnect() { Receive.Reset(); Poll(); }

        private void TickTimers(DateTime now)
        {
            if (Outbox.Tick(now).Count > 0) _bus.Publish(new OutboxChanged());
        }

        private List<ChatMessage> LoadTailForView(string lobbyId, int countedWanted)
        {
            if (lobbyId == null) return new List<ChatMessage>();
            // Only archived views merge grouped rooms; the live current lobby stays on its own file.
            var ids = IsArchivedView && lobbyId == ArchivedLobbyId ? RoomGroups.MembersOf(lobbyId) : new List<string> { lobbyId };
            if (ids.Count <= 1) return _history.LoadTail(lobbyId, countedWanted);
            var combined = new List<ChatMessage>();
            foreach (var id in ids) combined.AddRange(_history.LoadTail(id, countedWanted));
            combined = combined.OrderBy(m => m.TimestampUtc).ToList();
            int counted = 0, cut = 0;
            for (int i = combined.Count - 1; i >= 0; i--)
            {
                if (!HistoryWindow.Counts(combined[i])) continue;
                if (++counted > countedWanted) { cut = i + 1; break; }
            }
            if (cut > 0) combined.RemoveRange(0, cut);
            return combined;
        }

        private void LoadRoom(string lobbyId)
        {
            _messages.Clear(); _ids.Clear(); _countedAvailable = 0;
            Window.Collapse();
            if (lobbyId == null) return;
            // One more than the window so we know whether "더 보기" applies.
            var tail = LoadTailForView(lobbyId, Window.Limit + 1);
            _messages.AddRange(tail);
            foreach (var m in tail) _ids.Add(m.Id);
            _countedAvailable = tail.Count(HistoryWindow.Counts);
        }

        /// <summary>Keeps memory bounded: at most the window (+1 for the "more" check) of counted messages in memory.</summary>
        private void TrimToWindow()
        {
            int keep = Window.Limit + 1;
            int counted = 0, cut = 0;
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (!HistoryWindow.Counts(_messages[i])) continue;
                if (++counted > keep) { cut = i + 1; break; }
            }
            if (cut > 0) { _messages.RemoveRange(0, cut); _countedAvailable = keep; }
        }

        public bool CanShowMore { get { return Window.CanExpand(_countedAvailable); } }

        public void ShowMore()
        {
            Window.Expand();
            var id = ViewLobbyId;
            if (id == null) return;
            var tail = LoadTailForView(id, Window.Limit + 1);
            _messages.Clear(); _messages.AddRange(tail);
            foreach (var m in tail) _ids.Add(m.Id);
            _countedAvailable = tail.Count(HistoryWindow.Counts);
        }

        public void CollapseHistory()
        {
            Window.Collapse();
            TrimToWindow();
            _countedAvailable = Math.Min(_countedAvailable, Window.Limit + 1);
        }

        public void OpenArchived(string lobbyId, string name)
        {
            if (lobbyId == CurrentLobbyId) { CloseArchived(); return; }
            ArchivedLobbyId = lobbyId; ArchivedName = name;
            LoadRoom(lobbyId);
            _bus.Publish(new LobbyChanged { LobbyId = lobbyId });
        }

        public void CloseArchived()
        {
            ArchivedLobbyId = null; ArchivedName = null;
            LoadRoom(CurrentLobbyId);
            _bus.Publish(new LobbyChanged { LobbyId = CurrentLobbyId });
        }

        public TimelineOptions TimelineOptions(TimeZoneInfo tz, bool forExport) { return OptionsFrom(_prefs.Settings, tz, forExport); }

        /// <summary>Timeline options from settings (also used by the settings previews).</summary>
        public static TimelineOptions OptionsFrom(Settings s, TimeZoneInfo tz, bool forExport)
        {
            EmoteGroupMode mode;
            switch (s.Get("chat.emoteMode")) { case "A2": mode = EmoteGroupMode.ExtendFromLast; break; case "B": mode = EmoteGroupMode.AllOnOneLine; break; case "C": mode = EmoteGroupMode.PerPersonLine; break; default: mode = EmoteGroupMode.FixedFromFirst; break; }
            return new TimelineOptions
            {
                EmoteMode = mode,
                EmoteWindowSeconds = s.GetInt("chat.emoteWindow"),
                HideMeows = !forExport && s.GetBool("chat.hideMeows"),
                TimeZone = tz
            };
        }

        public List<TimelineItem> BuildTimeline(TimeZoneInfo tz)
        {
            var selected = Window.Select(_messages);
            return TimelineBuilder.Build(selected, TimelineOptions(tz, false));
        }

        public string Export(TimeZoneInfo tz)
        {
            var selected = Window.Select(_messages);
            var items = TimelineBuilder.Build(selected, TimelineOptions(tz, true));
            return ConversationExporter.Export(items, id => _prefs.Aliases.Get(id), tz, _prefs.L);
        }

        public int CharLimit { get { return Status != null && Status.CharLimit > 0 ? Status.CharLimit : 0; } }

        public SendBlock CheckSend(string text)
        {
            bool connected = Link == LinkState.Connected && CurrentLobbyId != null;
            bool canSend = Status != null && Status.CanSend && !Areas.SendLocked;
            return Outbox.Check(text, CharLimit, connected, canSend, IsArchivedView, _clock.UtcNow);
        }

        /// <summary>Sends; returns the block reason (the draft is kept by the caller unless None).</summary>
        public SendBlock Send(string text)
        {
            var block = CheckSend(text);
            if (block != SendBlock.None) return block;
            var id = Guid.NewGuid().ToString("N");
            var now = _clock.UtcNow;
            Outbox.Submit(id, text, now, CurrentLobbyId, Status == null ? null : Status.GameSession);
            try { _exchange.WriteSendRequest(id, CurrentLobbyId, text, now); }
            catch (Exception ex) { Outbox.MarkFailed(id, ex.GetType().Name + ": " + ex.Message); } // any failure keeps the text with "다시 보내기"
            _bus.Publish(new OutboxChanged());
            return SendBlock.None;
        }

        public SendBlock Retry(string requestId)
        {
            var o = Outbox.Find(requestId);
            if (o == null) return SendBlock.Empty;
            var block = CheckSend(o.Text);
            if (block != SendBlock.None && block != SendBlock.Duplicate) return block;
            var text = Outbox.TakeForRetry(requestId);
            if (text == null) return SendBlock.Empty;
            return Send(text);
        }

        public void DeleteFailed(string requestId) { if (Outbox.Delete(requestId)) _bus.Publish(new OutboxChanged()); }

        private List<Participant> ResolveCurrentPeople()
        {
            bool readable = Participants != null && Participants.Readable && Participants.LobbyId == CurrentLobbyId;
            return ParticipantList.Resolve(readable ? Participants.Members : null, readable, _messages);
        }

        public List<Participant> PeopleList()
        {
            var cur = ResolveCurrentPeople();
            if (cur.Count > 0) return cur;
            return Link != LinkState.Connected && _lastPeople.Count > 0 ? _lastPeople.ToList() : cur;
        }

        /// <summary>
        /// Saved rooms with user-defined room groups folded into one entry (95-SPEC 2.7).
        /// Rooms are never merged by name alone. If retention cleanup deleted one member of a group, the remaining
        /// member(s) are still shown.
        /// </summary>
        public List<RoomSummary> ListArchivedRooms()
        {
            var raw = _history.ListRooms().Where(r => r.LobbyId != CurrentLobbyId).ToList();
            var byId = raw.ToDictionary(r => r.LobbyId, r => r, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<RoomSummary>();
            foreach (var r in raw)
            {
                if (used.Contains(r.LobbyId)) continue;
                var g = RoomGroups.GroupOf(r.LobbyId);
                if (g == null)
                {
                    used.Add(r.LobbyId);
                    var single = new RoomSummary { LobbyId = r.LobbyId, Name = r.Name, LastUtc = r.LastUtc, MessageCount = r.MessageCount };
                    single.MemberLobbyIds.Add(r.LobbyId);
                    result.Add(single);
                    continue;
                }
                var members = g.LobbyIds.Where(id => byId.ContainsKey(id)).Select(id => byId[id]).OrderByDescending(m => m.LastUtc).ToList();
                foreach (var m in members) used.Add(m.LobbyId);
                if (members.Count == 0) continue;
                var newest = members[0];
                var name = !string.IsNullOrEmpty(g.Name) ? g.Name : members.Select(m => m.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
                var summary = new RoomSummary
                {
                    LobbyId = newest.LobbyId,
                    Name = name,
                    LastUtc = members.Max(m => m.LastUtc),
                    MessageCount = members.Sum(m => m.MessageCount)
                };
                summary.MemberLobbyIds.AddRange(members.Select(m => m.LobbyId));
                result.Add(summary);
            }
            return result.OrderByDescending(x => x.LastUtc).ToList();
        }

        /// <summary>Searches all rooms, folding grouped rooms under their group name and representative lobby ID (95-SPEC 2.7).</summary>
        public List<SearchHit> SearchAll(string query, int max)
        {
            var hits = _history.SearchAll(query, max);
            foreach (var h in hits)
            {
                var g = RoomGroups.GroupOf(h.LobbyId);
                if (g != null && !string.IsNullOrEmpty(g.Name)) h.RoomName = g.Name;
            }
            return hits;
        }

        public bool ParticipantsReadable { get { return Participants != null && Participants.Readable && Participants.LobbyId == CurrentLobbyId; } }

        public string AliasOf(string steamId) { return _prefs.Aliases.Get(steamId); }
        public string NameOf(string steamId, string original) { return DisplayName.Format(original, _prefs.Aliases.Get(steamId), _prefs.NameMode); }
    }
}
