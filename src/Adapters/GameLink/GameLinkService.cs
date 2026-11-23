using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.Application;
using NyangTalk.Domain.GameLink;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>
    /// Runs the static game check when the game build fingerprint changes (10-SPEC 6장), keeps the last report, and
    /// watches the BepInEx log for type-load errors (40-SPEC 1.1 5번). Never writes into the game folder.
    /// </summary>
    public sealed class GameLinkService
    {
        public const string StateDoc = "game-check";
        private readonly IDocumentStore _docs;
        private readonly string _contractJson;
        private readonly string _userProfile;
        public CheckReport LastReport { get; private set; }
        public GameFingerprint LastFingerprint { get; private set; }
        public int FullChecksRun { get; private set; }
        public string GameDir { get; private set; }

        public GameLinkService(IDocumentStore docs, string contractJson, string userProfile)
        {
            _docs = docs; _contractJson = contractJson; _userProfile = userProfile;
        }

        private GameFingerprint LoadStoredFingerprint(out CheckReport cachedSummary)
        {
            cachedSummary = null;
            string broken;
            var d = _docs.Load(StateDoc, out broken);
            if (d == null) return null;
            var fp = new GameFingerprint
            {
                Assembly = StampFrom(Json.Obj(d.ContainsKey("assembly") ? d["assembly"] : null)),
                GlobalGameManagers = StampFrom(Json.Obj(d.ContainsKey("ggm") ? d["ggm"] : null)),
                UnityPlayerVersion = Json.Str(d, "unity")
            };
            var areas = Json.Obj(d.ContainsKey("areas") ? d["areas"] : null);
            if (areas != null)
            {
                cachedSummary = new CheckReport { Fingerprint = fp };
                foreach (var kv in areas) if (kv.Value is bool) cachedSummary.Areas[kv.Key] = (bool)kv.Value;
                cachedSummary.ElapsedMs = Json.Long(d, "elapsed_ms", 0);
                foreach (var o in Json.List(d, "mix") ?? new List<object>())
                {
                    var m = Json.Obj(o);
                    if (m != null) cachedSummary.Mix.Add(new MixProblem { Assembly = Json.Str(m, "assembly"), Type = Json.Str(m, "type"), Member = Json.Str(m, "member"), RequiredBy = Json.Str(m, "required_by"), Fixable = Json.Bool(m, "fixable", false) });
                }
            }
            return fp;
        }

        private static FileStamp StampFrom(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new FileStamp { Size = Json.Long(d, "size", -1), MtimeTicks = Json.Long(d, "mtime", 0), Sha256 = Json.Str(d, "sha256") };
        }

        private static Dictionary<string, object> StampTo(FileStamp s)
        {
            return s == null ? null : new Dictionary<string, object> { { "size", s.Size }, { "mtime", s.MtimeTicks }, { "sha256", s.Sha256 } };
        }

        /// <summary>
        /// App start / folder change: compare the fingerprint (size and time first, hash only if those changed);
        /// run the full check only when it changed. Returns true when a full check ran.
        /// </summary>
        public bool Refresh(string gameDir, string fullProfileDir, AreaAvailability areas)
        {
            GameDir = gameDir;
            CheckReport cached;
            var stored = LoadStoredFingerprint(out cached);
            var fp = GameCheck.Fingerprint(gameDir, stored);
            LastFingerprint = fp;
            bool same = stored != null && cached != null && fp.Assembly != null && stored.Assembly != null && fp.Id == stored.Id && fp.UnityPlayerVersion == stored.UnityPlayerVersion;
            if (same)
            {
                LastReport = cached;
                Apply(cached, areas);
                return false;
            }
            var report = GameCheck.Run(gameDir, fullProfileDir, _contractJson, _userProfile);
            report.Fingerprint = fp;
            FullChecksRun++;
            LastReport = report;
            Apply(report, areas);
            var doc = new Dictionary<string, object>
            {
                { "assembly", StampTo(fp.Assembly) }, { "ggm", StampTo(fp.GlobalGameManagers) }, { "unity", fp.UnityPlayerVersion },
                { "areas", report.Areas.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "elapsed_ms", report.ElapsedMs }, { "mixing", (long)report.Mix.Count },
                { "mix", report.Mix.Select(m => (object)new Dictionary<string, object> { { "assembly", m.Assembly }, { "type", m.Type }, { "member", m.Member }, { "required_by", m.RequiredBy }, { "fixable", m.Fixable } }).ToList() }
            };
            _docs.Save(StateDoc, doc);
            return true;
        }

        private static void Apply(CheckReport r, AreaAvailability areas)
        {
            if (areas == null || r == null) return;
            foreach (var kv in r.Areas)
            {
                FeatureArea a;
                if (Enum.TryParse(kv.Key, out a)) areas.SetStatic(a, kv.Value);
            }
        }

        /// <summary>Type-load errors in the loader log of the current game run (only meaningful while the mod is on).</summary>
        /// <summary>true/false when the log was read; null when it exists but could not be read (unknown, not "no problem").</summary>
        public static bool? LogHasTypeLoadProblem(string gameDir)
        {
            var log = Path.Combine(gameDir, "BepInEx", "LogOutput.log");
            if (!File.Exists(log)) return false;
            try
            {
                using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    var lines = new List<string>();
                    string l;
                    while ((l = sr.ReadLine()) != null) lines.Add(l);
                    return GameLogScanner.HasTypeLoadProblem(lines);
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
