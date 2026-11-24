using System;
using System.Collections.Generic;

namespace NyangTalk.Shared
{
    /// <summary>The game's filter must approve before invoking any operation that can transmit a message.</summary>
    public static class FilteredSend
    {
        public static bool Run(bool? blocked, Action transmit)
        {
            if (blocked == true) return false;
            transmit();
            return true;
        }
    }

    /// <summary>
    /// One-picture-per-frame work list for emote icons (50-SPEC 2장) and cat portraits (70-SPEC 2.2.2): each key is made
    /// once, a failed key is retried when asked again, and after 3 failures it is given up for this game build.
    /// Used by the game component; tested with a fake game in the app tests.
    /// </summary>
    public sealed class PictureQueue
    {
        public const int MaxFailures = 3;
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly HashSet<string> _queued = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _done = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);
        public Func<string, bool> FileExists = f => true;

        public IDictionary<string, string> Done { get { return _done; } }

        /// <summary>Asks for a picture. Returns the file if already made (and still present).</summary>
        public string Request(string key)
        {
            string f;
            if (_done.TryGetValue(key, out f))
            {
                if (FileExists(f)) return f;
                _done.Remove(key); // cache was cleared (e.g. game build changed): make it again
            }
            int fails;
            if (_failures.TryGetValue(key, out fails) && fails >= MaxFailures) return null;
            if (_queued.Add(key)) _queue.Enqueue(key);
            return null;
        }

        /// <summary>The single key to render in this frame, or null.</summary>
        public string Next()
        {
            if (_queue.Count == 0) return null;
            var k = _queue.Dequeue();
            _queued.Remove(k);
            return k;
        }

        public void Succeeded(string key, string file) { _done[key] = file; _failures.Remove(key); }

        public void Failed(string key)
        {
            int n;
            _failures.TryGetValue(key, out n);
            _failures[key] = n + 1;
        }

        public bool GivenUp(string key) { int n; return _failures.TryGetValue(key, out n) && n >= MaxFailures; }

        /// <summary>Game build changed: forget everything so pictures are made again.</summary>
        public void Reset() { _queue.Clear(); _queued.Clear(); _done.Clear(); _failures.Clear(); }
    }

    /// <summary>What the game component can see and do on one chest shop (80-SPEC 1.1).</summary>
    public interface IChestShop
    {
        string Kind { get; }        // "Cosmetic" or "Emote"
        bool Ready { get; }         // ChestIsReady
        bool CanGet { get; }        // CanGetChest
        bool CanAfford { get; }     // CanAffordChest
        bool Opening { get; }       // _openingChest
        void Click();               // the game's own OnClick
    }

    /// <summary>Another lobby member's chest as the game shows it to me (80-SPEC 2장).</summary>
    public interface IGiftChest
    {
        string Member { get; }      // their Steam ID
        string Kind { get; }        // "Cosmetic" or "Emote"
        bool Ready { get; }         // shown, active and not clicked yet
        bool CanAfford { get; }     // my points are enough
        void Click();               // the game's own click on that chest
    }

    public sealed class ChestPermit
    {
        public bool Enabled;        // my own chests (1장)
        public bool Gifts;          // other members' chests with my points (2장)
        public HashSet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int DelaySeconds = 3;
        public int MaxPerHour = 20;
        public DateTime WrittenUtc;
        public string Session;     // lobby + game session the permit was written for

        public bool ValidAt(DateTime nowUtc) { return (Enabled || Gifts) && nowUtc >= WrittenUtc && nowUtc - WrittenUtc <= TimeSpan.FromSeconds(10); }
    }

    public sealed class ChestEvent
    {
        public DateTime TimeUtc;
        public string Kind;
        public string Member;      // null for my own chest
        public bool Called;        // OnClick was called without an exception
        public string Reason;
    }

    /// <summary>
    /// The automatic chest steps of 80-SPEC 1.2, checked once per second by the game component. Opens only through the
    /// game's click handler, one chest at a time, once per ready state, within the user's permit, delay and hourly cap.
    /// </summary>
    public sealed class ChestAutomaton
    {
        private readonly Dictionary<string, DateTime?> _readySince = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DateTime> _calls = new List<DateTime>();
        private bool _wasEnabled;
        private string _session;
        public string State = "off";      // off | waiting | opening | paused
        public string Reason;             // points | limit | permit | busy
        public readonly List<ChestEvent> Events = new List<ChestEvent>();

        public void Tick(DateTime nowUtc, ChestPermit permit, IList<IChestShop> shops, bool exchanging)
        {
            Tick(nowUtc, permit, shops, exchanging, null, null, null);
        }

        /// <summary>One check per second: my chests first, then other members' chests; at most one game call in total.</summary>
        public void Tick(DateTime nowUtc, ChestPermit permit, IList<IChestShop> shops, bool exchanging, IList<IGiftChest> gifts, string myId, ICollection<string> members)
        {
            bool enabled = permit != null && permit.ValidAt(nowUtc);
            if (!enabled)
            {
                State = permit != null && (permit.Enabled || permit.Gifts) ? "paused" : "off";
                Reason = permit != null && (permit.Enabled || permit.Gifts) ? "permit" : null;
                _wasEnabled = false;
                return;
            }
            // turning the feature on again, or a new lobby/game session, allows a new try (step 5)
            if (!_wasEnabled || permit.Session != _session) { _attempted.Clear(); _readySince.Clear(); }
            _wasEnabled = true;
            _session = permit.Session;
            _calls.RemoveAll(t => nowUtc - t >= TimeSpan.FromHours(1));

            bool busy = exchanging;
            foreach (var s in shops) if (s != null && s.Opening) busy = true;
            State = "waiting"; Reason = null;

            foreach (var s in shops)
            {
                if (!permit.Enabled) break;
                if (s == null || !permit.Kinds.Contains(s.Kind)) continue;
                if (!s.Ready) { _attempted.Remove(s.Kind); _readySince[s.Kind] = null; continue; } // ready released → may try again later
                if (_attempted.Contains(s.Kind)) continue;                                           // same ready state: never call twice
                DateTime? since;
                if (!_readySince.TryGetValue(s.Kind, out since) || since == null) _readySince[s.Kind] = since = nowUtc;
                if (!s.CanAfford) { State = "paused"; Reason = "points"; continue; }
                if (!s.CanGet) continue;
                if (_calls.Count >= permit.MaxPerHour) { State = "paused"; Reason = "limit"; continue; }
                if (busy) { State = "opening"; Reason = "busy"; return; }                          // one at a time (step 4)
                if (nowUtc - since.Value < TimeSpan.FromSeconds(permit.DelaySeconds)) continue;     // user delay (step 3)
                _attempted.Add(s.Kind);
                _calls.Add(nowUtc);
                var ev = new ChestEvent { TimeUtc = nowUtc, Kind = s.Kind };
                try { s.Click(); ev.Called = true; }
                catch (Exception ex) { ev.Reason = ex.GetType().Name; }
                Events.Add(ev);
                State = "opening";
                return;                                                                             // at most one call per tick
            }
            if (!permit.Gifts || gifts == null) return;
            foreach (var gch in gifts)
            {
                if (gch == null || !permit.Kinds.Contains(gch.Kind)) continue;
                // never my own cat, never someone who left the lobby (2장 동작 2)
                if (gch.Member == null || gch.Member == myId || members == null || !members.Contains(gch.Member)) continue;
                var key = "gift:" + gch.Member + ":" + gch.Kind;
                if (!gch.Ready) { _attempted.Remove(key); _readySince[key] = null; continue; }
                if (_attempted.Contains(key)) continue;                                             // same ready state: once (동작 4)
                DateTime? since;
                if (!_readySince.TryGetValue(key, out since) || since == null) _readySince[key] = since = nowUtc;
                if (!gch.CanAfford) { State = "paused"; Reason = "points"; continue; }               // 동작 3
                if (_calls.Count >= permit.MaxPerHour) { State = "paused"; Reason = "limit"; continue; } // shared cap (동작 6)
                if (busy) { State = "opening"; Reason = "busy"; return; }
                if (nowUtc - since.Value < TimeSpan.FromSeconds(permit.DelaySeconds)) continue;
                _attempted.Add(key);
                _calls.Add(nowUtc);
                var ev = new ChestEvent { TimeUtc = nowUtc, Kind = gch.Kind, Member = gch.Member };
                try { gch.Click(); ev.Called = true; }
                catch (Exception ex) { ev.Reason = ex.GetType().Name; }
                Events.Add(ev);
                State = "opening";
                return;
            }
        }
    }

    public static class ChatText
    {
        /// <summary>The punctuation the game's filter counts (50-SPEC 7장).</summary>
        public const string FilterPunctuation = "?!,.;:-";

        /// <summary>True when the same punctuation mark repeats more than <paramref name="allowed"/> times in a row.</summary>
        public static bool HasPunctuationRun(string text, int allowed)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int run = 0;
            char prev = '\0';
            foreach (var c in text)
            {
                if (FilterPunctuation.IndexOf(c) >= 0 && c == prev) run++;
                else run = FilterPunctuation.IndexOf(c) >= 0 ? 1 : 0;
                prev = c;
                if (run > allowed) return true;
            }
            return false;
        }
    }
}
