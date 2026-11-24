using System;
using System.Collections.Generic;
using System.Linq;
using NyangTalk.Domain.Conversation;

namespace NyangTalk.Domain.Sending
{
    public enum SendBlock { None, Empty, TooLong, Disconnected, NotAllowed, Archived, Duplicate }

    public enum OutgoingState
    {
        Sending,       // 보내는 중
        Delivered,     // 게임에 전달됨
        Merged,        // 방에서 내 메시지가 돌아와 일반 메시지로 합쳐짐
        Unconfirmed,   // 10초 안에 결과 없음: 확인 못 함
        Rejected,      // 게임이 거절
        Failed,        // 앱 쪽 실패(쓰기 실패 등)
        NotDelivered   // 게임의 채팅 필터에 걸려 방으로 가지 않음 (50-SPEC 7장)
    }

    public enum SendResultKind { Delivered, Rejected, Timeout, Filtered }

    public sealed class OutgoingMessage
    {
        public string RequestId;
        public string Text;
        public DateTime CreatedUtc;
        public OutgoingState State;
        public string Reason;
        public string LobbyId;      // where it was sent
        public string GameSession;  // which game run received the request
        public bool NeedsAction { get { return State == OutgoingState.Rejected || State == OutgoingState.Failed || State == OutgoingState.Unconfirmed || State == OutgoingState.NotDelivered; } }
        /// <summary>Show the "use at most 3 of the same punctuation mark" hint.</summary>
        public bool PunctuationHint { get { return State == OutgoingState.NotDelivered && (Reason == "punctuation" || NyangTalk.Domain.Sending.Punctuation.HasRun(Text, 3)); } }
        public bool SymbolsOnlyHint { get { return State == OutgoingState.NotDelivered && !string.IsNullOrWhiteSpace(Text) && !Text.Any(char.IsLetterOrDigit); } }
    }

    /// <summary>Send-side rules (50-SPEC 7장). Pure: time is passed in.</summary>
    /// <summary>Same punctuation mark more than allowed times in a row (the game's filter default allows 3).</summary>
    public static class Punctuation
    {
        public const string Marks = "?!,.;:-";
        public static bool HasRun(string text, int allowed)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int run = 0; char prev = '\0';
            foreach (var c in text)
            {
                bool p = Marks.IndexOf(c) >= 0;
                run = p && c == prev ? run + 1 : (p ? 1 : 0);
                prev = c;
                if (run > allowed) return true;
            }
            return false;
        }
    }

    public sealed class Outbox
    {
        public static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(1);
        public const int SendLogMax = 500;

        private readonly List<OutgoingMessage> _pending = new List<OutgoingMessage>();
        private readonly List<OutgoingMessage> _log = new List<OutgoingMessage>();

        public IList<OutgoingMessage> Pending { get { return _pending; } }
        /// <summary>My send history (text + result), newest last, at most 500 (50-SPEC 7장).</summary>
        public IList<OutgoingMessage> Log { get { return _log; } }

        /// <summary>Length counted in UTF-16 code units, the unit of the game's string length. Verified only by manual check.</summary>
        public static int Length(string text) { return text == null ? 0 : text.Length; }

        public SendBlock Check(string text, int limit, bool connected, bool canSend, bool archived, DateTime nowUtc)
        {
            if (archived) return SendBlock.Archived;
            if (!connected) return SendBlock.Disconnected;
            if (!canSend) return SendBlock.NotAllowed;
            if (string.IsNullOrWhiteSpace(text)) return SendBlock.Empty;
            if (limit > 0 && Length(text) > limit) return SendBlock.TooLong;
            foreach (var p in _pending.Concat(_log))
            {
                if (p.Text == text && nowUtc - p.CreatedUtc < DuplicateWindow && p.State != OutgoingState.Failed) return SendBlock.Duplicate;
            }
            return SendBlock.None;
        }

        public static readonly TimeSpan PairBefore = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan PairAfter = TimeSpan.FromMinutes(2);

        public OutgoingMessage Submit(string requestId, string text, DateTime nowUtc, string lobbyId = null, string gameSession = null)
        {
            var m = new OutgoingMessage { RequestId = requestId, Text = text, CreatedUtc = nowUtc, State = OutgoingState.Sending, LobbyId = lobbyId, GameSession = gameSession };
            _pending.Add(m);
            AddLog(m);
            return m;
        }

        private void AddLog(OutgoingMessage m)
        {
            _log.Add(m);
            while (_log.Count > SendLogMax) _log.RemoveAt(0);
        }

        public void LoadLog(IEnumerable<OutgoingMessage> entries)
        {
            _log.Clear();
            foreach (var e in entries) AddLog(e);
        }

        public OutgoingMessage Find(string requestId) { return _pending.FirstOrDefault(p => p.RequestId == requestId); }

        public void MarkFailed(string requestId, string reason)
        {
            var m = Find(requestId);
            if (m == null) return;
            m.State = OutgoingState.Failed; m.Reason = reason;
        }

        public void ApplyResult(string requestId, SendResultKind kind, string reason)
        {
            var m = Find(requestId);
            if (m == null || m.State == OutgoingState.Merged) return;
            switch (kind)
            {
                case SendResultKind.Delivered: if (m.State == OutgoingState.Sending || m.State == OutgoingState.Unconfirmed) m.State = OutgoingState.Delivered; break;
                case SendResultKind.Rejected: m.State = OutgoingState.Rejected; m.Reason = reason; break;
                case SendResultKind.Timeout: m.State = OutgoingState.Unconfirmed; m.Reason = reason; break;
                case SendResultKind.Filtered: m.State = OutgoingState.NotDelivered; m.Reason = reason; break;
            }
        }

        /// <summary>Marks results that never came as "확인 못 함". Returns the ones that changed.</summary>
        public List<OutgoingMessage> Tick(DateTime nowUtc)
        {
            var changed = new List<OutgoingMessage>();
            foreach (var m in _pending)
            {
                // no result, or delivered to the game but not back in the room within 10 s: "확인 못 함"
                if ((m.State == OutgoingState.Sending || m.State == OutgoingState.Delivered) && nowUtc - m.CreatedUtc > ResultTimeout)
                {
                    m.State = OutgoingState.Unconfirmed;
                    changed.Add(m);
                }
            }
            return changed;
        }

        /// <summary>When my own message comes back from the room, the oldest matching pending entry merges into it.</summary>
        /// <summary>
        /// Pairs a message that came back from the room with the message I sent (50-SPEC 7장, strict): same lobby, same game
        /// run, sender is my known Steam ID, exactly the same text, received between 1 s before and 2 min after sending;
        /// one received message pairs with one sent message, oldest first. Anything else is not paired.
        /// </summary>
        public OutgoingMessage OnEcho(ChatMessage m, string mySteamId)
        {
            if (m == null || string.IsNullOrEmpty(mySteamId) || m.SenderId != mySteamId) return null;
            var match = _pending
                .Where(p => p.State == OutgoingState.Sending || p.State == OutgoingState.Delivered || p.State == OutgoingState.Unconfirmed)
                .Where(p => p.Text == m.Body && p.LobbyId == m.LobbyId && p.GameSession != null && p.GameSession == m.GameSession)
                .Where(p => m.TimestampUtc >= p.CreatedUtc - PairBefore && m.TimestampUtc <= p.CreatedUtc + PairAfter)
                .OrderBy(p => p.CreatedUtc).FirstOrDefault();
            if (match == null) return null;
            match.State = OutgoingState.Merged;
            _pending.Remove(match);
            return match;
        }

        /// <summary>"다시 보내기": removes the failed entry; the caller submits its text again. The draft is untouched.</summary>
        public string TakeForRetry(string requestId)
        {
            var m = Find(requestId);
            if (m == null || !m.NeedsAction) return null;
            _pending.Remove(m);
            return m.Text;
        }

        public bool Delete(string requestId)
        {
            var m = Find(requestId);
            if (m == null || !m.NeedsAction) return false;
            return _pending.Remove(m);
        }

        public bool HasInFlight { get { return _pending.Any(p => p.State == OutgoingState.Sending); } }
    }
}
