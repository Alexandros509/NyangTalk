using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NyangTalk.Adapters.GameExchange;
using NyangTalk.Adapters.Storage;
using NyangTalk.App.Demo;
using NyangTalk.Application;
using NyangTalk.Domain.Appearance;
using NyangTalk.Domain.Update;

namespace NyangTalk.App
{
    /// <summary>Composition root: builds adapters and services for a data folder.</summary>
    public sealed class AppHost
    {
        public static readonly SemVersion Version = SemVersion.Parse(AppVersion.Text);

        /// <summary>
        /// Update signing public keys (current, next) as 64-byte X‖Y (20-SPEC 5장).
        /// Current: release key made 2026-11-20, fingerprint 9b80df6bfb8880871b373f62698a94375ad1582b79849470005ef18390dc2d90.
        /// The private key file stays outside this repository. To rotate, add the next key here first and ship it.
        /// </summary>
        public static readonly System.Collections.Generic.List<byte[]> UpdateKeys = new System.Collections.Generic.List<byte[]>
        {
            System.Convert.FromBase64String("vpOolWtFe7Z/qMS2j1BNU+ZssMA4uCvELvooDVL2UOgBuW9MBuuSzNx/Ck5H42Xh+E6pYc8uOuOdQJdTBSXj/Q==")
        };
        public readonly string DataRoot;
        public readonly EventBus Bus = new EventBus();
        public readonly IClock Clock;
        public readonly JsonDocumentStore Docs;
        public readonly FileExchange Exchange;
        public readonly FileHistoryStore History;
        public readonly PreferencesService Prefs;
        public readonly ChatService Chat;
        public readonly PortraitCache Portraits = new PortraitCache();
        public readonly FakeGame Demo;      // non-null only in --demo mode
        public readonly List<string> Log = new List<string>();

        public AppHost(string dataRoot, IClock clock, bool demo)
        {
            DataRoot = dataRoot;
            Clock = clock;
            Directory.CreateDirectory(dataRoot);
            Docs = new JsonDocumentStore(Path.Combine(dataRoot, "data"));
            Exchange = new FileExchange(Path.Combine(dataRoot, "exchange"), Docs);
            History = new FileHistoryStore(Path.Combine(dataRoot, "history"));
            Prefs = new PreferencesService(Docs);
            Prefs.Load();
            Chat = new ChatService(Exchange, History, Clock, Bus, Prefs, Docs);
            if (demo) Demo = new FakeGame(Path.Combine(dataRoot, "exchange"));
            LogFile = new LogFile(Path.Combine(dataRoot, "logs"));
            PrivateWords = KnownWords;
        }

        /// <summary>95-SPEC 2.5: the log file in the app data (5 MB, two earlier files).</summary>
        public readonly LogFile LogFile;
        /// <summary>Chat texts and names that must never reach the log; replaced in checks.</summary>
        public Func<IEnumerable<string>> PrivateWords;
        public string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private string[] _lastWords = new string[0];

        /// <summary>
        /// Every log line is cleaned before it is kept in memory or written: user folder, chat texts and names the app
        /// knows, full Steam IDs. Callable from worker threads; if the word list is being changed meanwhile, the last
        /// complete list is used.
        /// </summary>
        public void AddLog(string line)
        {
            string[] words;
            try { words = (PrivateWords == null ? Enumerable.Empty<string>() : PrivateWords()).ToArray(); _lastWords = words; }
            catch (Exception) { words = _lastWords; }   // list changed on the UI thread while reading
            var clean = NyangTalk.Domain.Preferences.DiagnosticsSanitizer.LogLine(line, UserProfile, words);
            var now = DateTime.Now;
            lock (Log)
            {
                Log.Add(now.ToString("HH:mm:ss") + " " + clean);
                while (Log.Count > 200) Log.RemoveAt(0);
            }
            LogFile.Write(now.ToString("yyyy-MM-dd HH:mm:ss") + " " + clean);
        }

        /// <summary>The newest log lines for 진단 내용 (from the file, so the previous run is included).</summary>
        public List<string> RecentLog(int count)
        {
            var lines = LogFile.Tail(count);
            if (lines.Count > 0) return lines;
            lock (Log) return Log.Skip(Math.Max(0, Log.Count - count)).ToList();
        }

        private IEnumerable<string> KnownWords()
        {
            var words = new List<string>();
            foreach (var m in Chat.Messages.ToArray()) { words.Add(m.Body); words.Add(m.SenderName); }
            foreach (var o in Chat.Outbox.Pending.ToArray()) words.Add(o.Text);
            var p = Chat.Participants;
            if (p != null) foreach (var m in p.Members.ToArray()) words.Add(m.Name);
            foreach (var a in Prefs.Aliases.All.Values.ToArray()) words.Add(a);
            return words;
        }

        public static string DefaultRoot() { return AppPaths.Root(); }
    }

    public static class AppVersion
    {
        /// <summary>Full SemVer shown on screen; file version is MAJOR.MINOR.PATCH.0 (01-OVERVIEW 2.2).</summary>
        public const string Text = "0.1.8";
    }
}
