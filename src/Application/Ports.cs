using System;
using System.Collections.Generic;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Application
{
    public interface IClock { DateTime UtcNow { get; } }

    public sealed class SystemClock : IClock { public DateTime UtcNow { get { return DateTime.UtcNow; } } }

    /// <summary>Status written by the game component every second (40-SPEC 2장).</summary>
    public sealed class StatusSnapshot
    {
        public string Schema;
        public string ProducerVersion;
        public DateTime WrittenUtc;
        public bool Ready;
        public LobbyInfo Lobby;        // null when not in a lobby
        public bool CanSend;
        public int CharLimit;          // 0 = unknown
        public string Error;
        public string MySteamId;
        public Dictionary<string, bool> Areas = new Dictionary<string, bool>();
        public string GameSession;
        /// <summary>Emote id → picture file (absolute, or null) and item name (or null). 50-SPEC 2장.</summary>
        public Dictionary<string, EmoteInfo> Emotes = new Dictionary<string, EmoteInfo>();
        public string ChestState;
        public string ChestReason;
        /// <summary>Chat events the component's hook received in this game run (95-SPEC 2.4); null when not reported.</summary>
        public long? ChatEvents;
    }

    public sealed class EmoteInfo { public string File; public string Name; public bool GivenUp; }

    public sealed class ChestLogRow { public DateTime TimeUtc; public string Kind; public string Member; public bool Called; public string Reason; }

    public sealed class ParticipantsSnapshot
    {
        public string LobbyId;
        public bool Readable;
        public List<Participant> Members = new List<Participant>();
    }

    public sealed class AppearanceRow
    {
        public string SteamId;
        public string Skin;
        public string Hat;
        public string PortraitFile;    // absolute path or null
    }

    public sealed class SendResultRow
    {
        public string RequestId;
        public SendResultKind Kind;
        public string Reason;
    }

    /// <summary>Port to the exchange folder shared with the game component.</summary>
    public interface IGameExchange
    {
        /// <summary>null when the status file does not exist or cannot be read right now.</summary>
        StatusSnapshot ReadStatus();
        /// <summary>Newly appended complete lines of the lobby file; partial last line is left for the next call.</summary>
        List<ChatMessage> ReadNewMessages(string lobbyId);
        /// <summary>Marks what the last ReadNewMessages returned as consumed. Called only after the batch is stored.</summary>
        void CommitRead(string lobbyId);
        ParticipantsSnapshot ReadParticipants();
        List<AppearanceRow> ReadAppearance();
        void WriteSendRequest(string requestId, string lobbyId, string text, DateTime nowUtc);
        List<SendResultRow> ReadResults();
        void WritePermit(IDictionary<string, object> permit, DateTime nowUtc);
        List<ChestLogRow> ReadChestLog();
    }

    public sealed class RoomSummary
    {
        public string LobbyId;
        public string Name;
        public DateTime LastUtc;
        public int MessageCount;
        /// <summary>All existing lobby IDs combined into this entry when grouped (95-SPEC 2.7); otherwise just <see cref="LobbyId"/>.</summary>
        public readonly List<string> MemberLobbyIds = new List<string>();
        public bool IsGrouped { get { return MemberLobbyIds.Count > 1; } }
    }

    public sealed class SearchHit
    {
        public string LobbyId;
        public string RoomName;
        public ChatMessage Message;
    }

    /// <summary>Per-room conversation records on this PC (90-SPEC 2장).</summary>
    public interface IHistoryStore
    {
        void Append(string lobbyId, string lobbyName, IList<ChatMessage> messages);
        /// <summary>Reads from the end only as much as needed: up to <paramref name="countedWanted"/> non-meow messages.</summary>
        List<ChatMessage> LoadTail(string lobbyId, int countedWanted);
        List<RoomSummary> ListRooms();
        void DeleteRoom(string lobbyId);
        int Cleanup(DateTime nowUtc, int retentionDays);
        List<SearchHit> SearchAll(string query, int max);
    }

    /// <summary>Small JSON documents in the app data folder (settings, aliases, colours, send log, consent...).</summary>
    public interface IDocumentStore
    {
        /// <summary>Returns null when missing. If the file is damaged it is renamed to .broken-&lt;date&gt; and <paramref name="brokenCopy"/> is set.</summary>
        IDictionary<string, object> Load(string name, out string brokenCopy);
        /// <summary>True when the document file exists (so a null Load means unreadable or damaged, not missing).</summary>
        bool Exists(string name);
        /// <summary>True when an earlier load found the document damaged and kept a .broken copy.</summary>
        bool HasBrokenCopy(string name);
        /// <summary>Atomic write with short retries; false when it could not be saved.</summary>
        bool Save(string name, IDictionary<string, object> doc);
    }
}
