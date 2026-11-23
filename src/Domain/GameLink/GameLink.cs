using System;
using System.Collections.Generic;
using System.Linq;
using NyangTalk.Domain.Update;

namespace NyangTalk.Domain.GameLink
{
    /// <summary>Feature areas judged separately (10-SPEC 3장).</summary>
    public enum FeatureArea { ChatReceive, ChatSend, LobbyInfo, Participants, Appearance, Chest }

    public enum LinkState
    {
        NotInstalled,
        Waiting,            // installed, no status yet
        Connected,
        Disconnected,       // status older than 6 s
        NeedsComponentUpdate, // schema MAJOR differs
        NeedsReinstall      // producer_version out of accepted range
    }

    public enum ExchangeVerdict { Ok, Invalid, SchemaMajorMismatch, ProducerOutOfRange }

    /// <summary>Exchange format compatibility (40-SPEC 4장).</summary>
    public static class ExchangeCompatibility
    {
        public const string AppSchema = "1.0.0";
        public static readonly SemVersion KnownSchema = SemVersion.Parse(AppSchema);
        public const string AcceptedProducerRange = ">=0.1.0 <0.2.0";

        public static ExchangeVerdict Check(string schema, string producerVersion)
        {
            SemVersion s, p;
            if (!SemVersion.TryParse(schema, out s)) return ExchangeVerdict.Invalid;
            if (s.Major != KnownSchema.Major) return ExchangeVerdict.SchemaMajorMismatch;
            if (!SemVersion.TryParse(producerVersion, out p)) return ExchangeVerdict.Invalid;
            if (!VersionRange.Parse(AcceptedProducerRange).Contains(p)) return ExchangeVerdict.ProducerOutOfRange;
            return ExchangeVerdict.Ok;
        }
    }

    public static class LinkHealth
    {
        public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);

        /// <summary>Status file not refreshed for more than 6 s → "게임 연결 끊김". Unknown is never reported as connected.</summary>
        public static LinkState Evaluate(bool installed, DateTime? statusWrittenUtc, ExchangeVerdict verdict, DateTime nowUtc)
        {
            if (verdict == ExchangeVerdict.SchemaMajorMismatch) return LinkState.NeedsComponentUpdate;
            if (verdict == ExchangeVerdict.ProducerOutOfRange) return LinkState.NeedsReinstall;
            if (statusWrittenUtc == null) return installed ? LinkState.Waiting : LinkState.NotInstalled;
            if (nowUtc - statusWrittenUtc.Value > StaleAfter) return LinkState.Disconnected;
            if (verdict != ExchangeVerdict.Ok) return LinkState.Disconnected;
            return LinkState.Connected;
        }
    }

    /// <summary>Per-area availability merged from the static check and the plugin's runtime report (plugin wins, 10-SPEC 6장).</summary>
    public sealed class AreaAvailability
    {
        private readonly Dictionary<FeatureArea, bool> _static = new Dictionary<FeatureArea, bool>();
        private readonly Dictionary<FeatureArea, bool> _runtime = new Dictionary<FeatureArea, bool>();

        public void SetStatic(FeatureArea a, bool ok) { _static[a] = ok; }
        public void SetRuntime(FeatureArea a, bool ok) { _runtime[a] = ok; }
        public void ClearRuntime() { _runtime.Clear(); }

        public bool IsAvailable(FeatureArea a)
        {
            bool v;
            if (_runtime.TryGetValue(a, out v)) return v;
            if (_static.TryGetValue(a, out v)) return v;
            return true; // not judged yet: do not block, but the UI does not claim "verified"
        }

        public bool SendLocked { get { return !IsAvailable(FeatureArea.ChatSend); } }
        public bool ReceiveWorks { get { return IsAvailable(FeatureArea.ChatReceive); } }
        public IEnumerable<FeatureArea> Broken { get { return Enum.GetValues(typeof(FeatureArea)).Cast<FeatureArea>().Where(a => !IsAvailable(a)); } }
    }

    /// <summary>Game build fingerprint (10-SPEC 3장). Hashes are computed only when size or time changed.</summary>
    public sealed class FileStamp
    {
        public long Size;
        public long MtimeTicks;
        public string Sha256;
        public bool SameStat(FileStamp o) { return o != null && o.Size == Size && o.MtimeTicks == MtimeTicks; }
    }

    public sealed class GameFingerprint
    {
        public FileStamp Assembly;
        public FileStamp GlobalGameManagers;
        public string UnityPlayerVersion;

        public string Id
        {
            get { return (Assembly == null ? "-" : Assembly.Sha256) + ":" + (GlobalGameManagers == null ? "-" : GlobalGameManagers.Sha256); }
        }

        public bool StatEquals(GameFingerprint o)
        {
            return o != null && Assembly != null && GlobalGameManagers != null && Assembly.SameStat(o.Assembly) && GlobalGameManagers.SameStat(o.GlobalGameManagers);
        }
    }

    /// <summary>Shows a notice only when the fingerprint or the result changed (10-SPEC 7장).</summary>
    public sealed class NoticeDeduper
    {
        private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        public bool ShouldShow(string fingerprintId, string resultKey) { return _shown.Add(fingerprintId + "\u0001" + resultKey); }
        public IEnumerable<string> Shown { get { return _shown; } }
        public void Load(IEnumerable<string> keys) { foreach (var k in keys) _shown.Add(k); }
    }

    /// <summary>
    /// 95-SPEC 2.4 health signal, judged from both sides' numbers: the game component reports how many chat events its
    /// hook received (status "chat_events"), the app counts the chat lines it read. Warns only when, while connected, the
    /// app read nothing for <see cref="Window"/> and the component's count rose by at least <see cref="MinMissing"/> in that
    /// time. The game log is not used (chat text is not assumed to be logged).
    /// MinMissing is 3, not 1: the status file is written once per game tick after the chat line, so the count the app
    /// sees when it reads a line can be one behind; one late count plus one lost line must not warn.
    /// </summary>
    public sealed class ReceiveWatch
    {
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(20);
        public const int MinMissing = 3;

        private string _key;
        private long? _eventsAtLastRead;
        private long _appCount;
        private DateTime _lastReadUtc;

        public bool Warning { get; private set; }
        public long Missing { get; private set; }

        public void Reset() { _key = null; _eventsAtLastRead = null; Warning = false; Missing = 0; }

        /// <param name="key">game session and lobby; a change starts over.</param>
        /// <param name="componentEvents">null when the component does not report it (older component): no judgement.</param>
        /// <param name="appReceived">total chat lines the app has read so far.</param>
        public bool Observe(DateTime nowUtc, bool connected, string key, long? componentEvents, long appReceived)
        {
            if (!connected || componentEvents == null) { Reset(); return false; }
            long ev = componentEvents.Value;
            if (_eventsAtLastRead == null || key != _key || ev < _eventsAtLastRead.Value || appReceived != _appCount)
            {
                _key = key; _eventsAtLastRead = ev; _appCount = appReceived; _lastReadUtc = nowUtc;
                Warning = false; Missing = 0;
                return false;
            }
            Missing = ev - _eventsAtLastRead.Value;
            Warning = Missing >= MinMissing && nowUtc - _lastReadUtc >= Window;
            return Warning;
        }
    }

    /// <summary>Type-load failures in the game log while the mod is on (40-SPEC 1.1 5번).</summary>
    public static class GameLogScanner
    {
        public static readonly string[] Markers = { "TypeLoadException", "VTable setup", "invalid vtable" };

        public static bool HasTypeLoadProblem(IEnumerable<string> lines)
        {
            foreach (var l in lines)
                foreach (var m in Markers)
                    if (l.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
