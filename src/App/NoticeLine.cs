using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.App
{
    /// <summary>What a notice is about; a smaller number wins the one line (95-SPEC 2.6: connection problems first).</summary>
    public enum NoticeKind { Connection = 0, Update = 1, Health = 2, Info = 3 }

    public sealed class NoticeEntry
    {
        public string Key;          // dismissing remembers this key
        public NoticeKind Kind;
        public string Text;
        public string ActionText;   // null: no button
        public Action Action;
    }

    /// <summary>
    /// 95-SPEC 2.6: the single notice line at the top of the chat — one sentence and at most one action button. Each kind
    /// keeps its latest entry; the line shows the most important one that the user has not closed.
    /// </summary>
    public sealed class NoticeLine
    {
        private readonly Dictionary<NoticeKind, NoticeEntry> _slots = new Dictionary<NoticeKind, NoticeEntry>();
        private readonly HashSet<string> _dismissed = new HashSet<string>();
        public event Action Changed;

        public void Set(NoticeEntry e)
        {
            NoticeEntry old;
            if (_slots.TryGetValue(e.Kind, out old) && old.Key == e.Key && old.Text == e.Text && old.ActionText == e.ActionText) { old.Action = e.Action; return; }
            _slots[e.Kind] = e;
            Raise();
        }

        public void Clear(NoticeKind kind) { if (_slots.Remove(kind)) Raise(); }

        public void Dismiss(string key) { if (key != null && _dismissed.Add(key)) Raise(); }

        public bool IsDismissed(string key) { return _dismissed.Contains(key); }

        public NoticeEntry Current
        {
            get { return _slots.Values.Where(e => !_dismissed.Contains(e.Key)).OrderBy(e => (int)e.Kind).FirstOrDefault(); }
        }

        public NoticeEntry Get(NoticeKind kind) { NoticeEntry e; return _slots.TryGetValue(kind, out e) ? e : null; }

        private void Raise() { var h = Changed; if (h != null) h(); }
    }

    /// <summary>95-SPEC 2.2: when the automatic update check runs — 30 s after start, then every 6 hours; a failure waits for the next turn.</summary>
    public sealed class UpdateSchedule
    {
        public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30), Every = TimeSpan.FromHours(6);
        private DateTime _next;

        public UpdateSchedule(DateTime startUtc) { _next = startUtc + FirstDelay; }

        public DateTime Next { get { return _next; } }

        /// <summary>True when a check is due now; the next one is then planned 6 hours later whatever the result.</summary>
        public bool Due(DateTime nowUtc)
        {
            if (nowUtc < _next) return false;
            _next = nowUtc + Every;
            return true;
        }
    }
}
