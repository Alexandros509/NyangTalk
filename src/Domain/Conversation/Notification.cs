using System;

namespace NyangTalk.Domain.Conversation
{
    public enum NotifyMode { Off = 0, Flash = 1, FlashAndBadge = 2 }

    /// <summary>New-message notification rules (70-SPEC 1장). No composing or read indicators exist anywhere (01-OVERVIEW 3장).</summary>
    public sealed class UnreadTracker
    {
        public NotifyMode Mode = NotifyMode.FlashAndBadge;
        private int _unread;
        private bool _flashing;

        public int Unread { get { return Mode == NotifyMode.FlashAndBadge ? _unread : 0; } }
        public bool Flashing { get { return Mode != NotifyMode.Off && _flashing; } }

        /// <summary>Only other people's normal text and emotes notify; never mine, meows or initial history.</summary>
        public static bool IsNotifiable(ChatMessage m, bool isInitialLoad)
        {
            if (isInitialLoad || m == null || m.IsMine) return false;
            if (MeowCatalog.IsMeow(m)) return false;
            return true;
        }

        public void OnMessage(ChatMessage m, bool isInitialLoad, bool windowActive, bool atBottom)
        {
            if (!IsNotifiable(m, isInitialLoad)) return;
            if (windowActive && atBottom) return; // already read
            if (Mode == NotifyMode.Off) return;
            _unread++;
            if (!windowActive) _flashing = true;
        }

        /// <summary>Activating the window stops flashing; the badge stays while the user reads older messages.</summary>
        public void OnActivated(bool atBottom)
        {
            _flashing = false;
            if (atBottom) _unread = 0;
        }

        public void OnScrolled(bool windowActive, bool atBottom)
        {
            if (windowActive && atBottom) _unread = 0;
        }
    }
}
