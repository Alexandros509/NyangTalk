using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NyangTalk.Application;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>An earlier version's move inside the game folder (plugins only), kept so it can still be undone.</summary>
    public sealed class ParkedMod
    {
        public string From, To;
        public DateTime TimeUtc;
    }

    public sealed class ParkedFile
    {
        public string Rel;       // relative to the game folder
        public string Sha256;
        public long Size;
    }

    public sealed class ParkedSet
    {
        public string Folder;    // under the app data, holds the files at their relative paths
        public DateTime TimeUtc;
        public readonly List<ParkedFile> Files = new List<ParkedFile>();
    }

    public enum ParkOutcome { Done, NothingToPark, GameRunning, Failed }

    public sealed class ParkResult
    {
        public ParkOutcome Outcome;
        public List<string> Moved = new List<string>();
        public List<string> Skipped = new List<string>();
        public string Why;
        public string Folder;   // where the files are kept (app data), for the message
    }

    /// <summary>
    /// "이전 모드 옮기고 새로 설치" (40-SPEC 1.1·6장, 2026-11-20 user decision): every BepInEx-related file in the game folder
    /// that NyangTalk did not install — the loader entry files, the doorstop settings, and everything in the BepInEx folder
    /// that is not NyangTalk's, an earlier mod's own data included — is moved out of the game folder into the app data,
    /// file by file with its SHA-256 recorded. Nothing is deleted. Restoring puts each file back after checking its hash and
    /// never overwrites a file that appeared at its place meanwhile.
    /// </summary>
    public sealed class ModParking
    {
        public const string Doc = "parked-mods";
        public const string OldParkDir = @"BepInEx\NyangTalk-parked";   // the first version moved plugins here
        /// <summary>Loader entry files of BepInEx/UnityDoorstop in the game folder.</summary>
        public static readonly string[] LoaderEntryFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" };
        private readonly IDocumentStore _docs;
        private readonly string _parkRoot;
        public Func<bool> GameRunning = () => false;
        public Func<DateTime> Now = () => DateTime.UtcNow;
        /// <summary>Paths (relative to the game folder) that NyangTalk installed: never moved. Null when the install record cannot be read (then nothing moves).</summary>
        public Func<IEnumerable<string>> Owned = () => Enumerable.Empty<string>();
        public const string OwnPluginDir = @"BepInEx\plugins\NyangTalk";

        public ModParking(IDocumentStore docs, string parkRoot) { _docs = docs; _parkRoot = parkRoot; }

        // ---------------------------------------------------------------- what would move

        private static bool IsBepInExChangelog(string path)
        {
            try
            {
                using (var r = new StreamReader(path))
                {
                    var buf = new char[4096];
                    int n = r.Read(buf, 0, buf.Length);
                    var s = new string(buf, 0, n);
                    return s.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("Doorstop", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("commits since v", StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("Changelog (excluding merges)", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>Files (relative paths) that "옮기기" would move.</summary>
        public List<string> Foreign(string gameDir)
        {
            var ownedList = Owned();
            if (ownedList == null) throw new InvalidDataException("install record unreadable");
            var owned = new HashSet<string>(ownedList, StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var f in LoaderEntryFiles)
                if (File.Exists(Path.Combine(gameDir, f)) && !owned.Contains(f)) list.Add(f);
            var changelog = Path.Combine(gameDir, "changelog.txt");   // BepInEx's own, next to the loader; the game's would not mention BepInEx/Doorstop
            if (File.Exists(changelog) && !owned.Contains("changelog.txt") && IsBepInExChangelog(changelog)) list.Add("changelog.txt");
            if (Directory.Exists(gameDir))
                foreach (var f in Directory.GetFiles(gameDir, "*-install.json").Concat(Directory.GetFiles(gameDir, "preloader_*.log")).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(f);
                    if (!owned.Contains(name)) list.Add(name);
                }
            var bep = Path.Combine(gameDir, "BepInEx");
            if (Directory.Exists(bep))
                foreach (var f in FilesNoLinks(bep).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var rel = f.Substring(gameDir.TrimEnd('\\').Length + 1);
                    // NyangTalk's own plugin folder (also what the plugin writes there at run time) is never moved
                    if (rel.StartsWith(OwnPluginDir + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!owned.Contains(rel)) list.Add(rel);
                }
            return list;
        }

        /// <summary>Files under a folder without following folder links (junctions, symbolic links) out of the game folder.</summary>
        private static IEnumerable<string> FilesNoLinks(string dir)
        {
            foreach (var f in Directory.GetFiles(dir)) yield return f;
            foreach (var d in Directory.GetDirectories(dir))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var f in FilesNoLinks(d)) yield return f;
            }
        }

        /// <summary>A recorded path must stay inside its base folder.</summary>
        private static bool Inside(string root, string rel)
        {
            if (string.IsNullOrEmpty(rel) || Path.IsPathRooted(rel) || rel.Contains("..")) return false;
            var full = Path.GetFullPath(Path.Combine(root, rel));
            return full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Other plugins and patchers by name, for the screen (the files themselves are listed by <see cref="Foreign"/>).</summary>
        public static List<string> Candidates(string gameDir, out List<string> notMovable)
        {
            var list = new List<string>();
            notMovable = new List<string>();
            foreach (var sub in new[] { "plugins", "patchers" })
            {
                var d = Path.Combine(gameDir, "BepInEx", sub);
                if (!Directory.Exists(d)) continue;
                foreach (var e in Directory.GetFileSystemEntries(d).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(e);
                    if (sub == "plugins" && string.Equals(name, "NyangTalk", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(@"BepInEx\" + sub + @"\" + name);
                }
            }
            if (Directory.Exists(Path.Combine(gameDir, "MelonLoader"))) notMovable.Add("MelonLoader");
            foreach (var proxy in new[] { "version.dll", "winmm.dll", "dinput8.dll", "dxgi.dll" })
                if (File.Exists(Path.Combine(gameDir, proxy))) notMovable.Add(proxy);
            return list;
        }

        // ---------------------------------------------------------------- record

        /// <summary>True when the move record exists but cannot be read; then nothing is moved or restored (it would be overwritten).</summary>
        public bool RecordBroken { get; private set; }

        private Dictionary<string, object> LoadDoc()
        {
            string broken;
            var d = _docs.Load(Doc, out broken);
            RecordBroken = d == null && (broken != null || _docs.HasBrokenCopy(Doc));
            return d == null ? new Dictionary<string, object>() : new Dictionary<string, object>(d);
        }

        public List<ParkedMod> OldMoves()
        {
            var list = new List<ParkedMod>();
            object v;
            if (!LoadDoc().TryGetValue("moves", out v) || !(v is IList<object>)) return list;
            foreach (var o in (IList<object>)v)
            {
                var m = o as IDictionary<string, object>;
                if (m == null) continue;
                DateTime t;
                Exchange.TryIso(Json.Str(m, "time_utc"), out t);
                list.Add(new ParkedMod { From = Json.Str(m, "from"), To = Json.Str(m, "to"), TimeUtc = t });
            }
            return list.Where(x => x.From != null && x.To != null).ToList();
        }

        public List<ParkedSet> Sets()
        {
            var list = new List<ParkedSet>();
            object v;
            if (!LoadDoc().TryGetValue("sets", out v) || !(v is IList<object>)) return list;
            foreach (var o in (IList<object>)v)
            {
                var m = o as IDictionary<string, object>;
                if (m == null || Json.Str(m, "folder") == null) continue;
                DateTime t;
                Exchange.TryIso(Json.Str(m, "time_utc"), out t);
                var set = new ParkedSet { Folder = Json.Str(m, "folder"), TimeUtc = t };
                object fs;
                if (m.TryGetValue("files", out fs) && fs is IList<object>)
                    foreach (var fo in (IList<object>)fs)
                    {
                        var f = fo as IDictionary<string, object>;
                        if (f == null || Json.Str(f, "rel") == null) continue;
                        set.Files.Add(new ParkedFile { Rel = Json.Str(f, "rel"), Sha256 = Json.Str(f, "sha256"), Size = Json.Long(new Dictionary<string, object>(f), "size", -1) });
                    }
                if (set.Files.Count > 0) list.Add(set);
            }
            return list;
        }

        /// <summary>Anything to bring back (this version's sets or the earlier plugin moves).</summary>
        public bool HasParked { get { return Sets().Count > 0 || OldMoves().Count > 0; } }

        private bool Save(List<ParkedSet> sets, List<ParkedMod> oldMoves)
        {
            var doc = new Dictionary<string, object>
            {
                { "moves", oldMoves.Select(m => (object)new Dictionary<string, object> { { "from", m.From }, { "to", m.To }, { "time_utc", Exchange.Iso(m.TimeUtc) } }).ToList() },
                { "sets", sets.Where(s => s.Files.Count > 0).Select(s => (object)new Dictionary<string, object>
                    {
                        { "folder", s.Folder }, { "time_utc", Exchange.Iso(s.TimeUtc) },
                        { "files", s.Files.Select(f => (object)new Dictionary<string, object> { { "rel", f.Rel }, { "sha256", f.Sha256 }, { "size", f.Size } }).ToList() }
                    }).ToList() }
            };
            return _docs.Save(Doc, doc);
        }

        public static string Sha256(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // ---------------------------------------------------------------- move out

        /// <summary>Moves the earlier BepInEx setup out of the game folder (see the class summary).</summary>
        public ParkResult ParkAll(string gameDir)
        {
            var res = new ParkResult();
            if (GameRunning()) { res.Outcome = ParkOutcome.GameRunning; return res; }
            Sets();
            if (RecordBroken) { res.Outcome = ParkOutcome.Failed; res.Why = "move record unreadable"; return res; }
            // the first version's plugin moves go back first, so they are carried along with everything else
            if (OldMoves().Count > 0)
            {
                var back = RestoreOld(gameDir, res);
                if (!back) { res.Outcome = ParkOutcome.Failed; res.Why = "earlier move could not be undone: " + res.Why; return res; }
            }
            List<string> files;
            try { files = Foreign(gameDir); }
            catch (InvalidDataException ex) { res.Outcome = ParkOutcome.Failed; res.Why = ex.Message; return res; }
            if (files.Count == 0) { res.Outcome = ParkOutcome.NothingToPark; return res; }
            var now = Now();
            var set = new ParkedSet { Folder = Path.Combine(_parkRoot, now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6)), TimeUtc = now };
            var sets = Sets();
            sets.Add(set);
            res.Folder = set.Folder;
            foreach (var rel in files)
            {
                var src = Path.Combine(gameDir, rel);
                var dst = Path.Combine(set.Folder, rel);
                bool copied = false;
                try
                {
                    if (GameRunning()) { res.Outcome = ParkOutcome.Failed; res.Why = "the game started"; break; }
                    var info = new FileInfo(src);
                    long len = info.Length; var written = info.LastWriteTimeUtc;
                    var sha = Sha256(src);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, false);
                    copied = true;
                    if (Sha256(dst) != sha) throw new IOException("copy differs: " + rel);
                    info.Refresh();
                    if (info.Length != len || info.LastWriteTimeUtc != written || Sha256(src) != sha) throw new IOException("changed while moving: " + rel);
                    // record first, then delete: a crash in between leaves the original in place and the copy recorded
                    set.Files.Add(new ParkedFile { Rel = rel, Sha256 = sha, Size = len });
                    if (!Save(sets, new List<ParkedMod>())) { set.Files.RemoveAt(set.Files.Count - 1); throw new IOException("record not saved"); }
                    File.Delete(src);
                    res.Moved.Add(rel);
                }
                catch (Exception ex)
                {
                    if (copied && !res.Moved.Contains(rel) && !set.Files.Any(f => f.Rel == rel)) { try { File.Delete(dst); } catch (Exception) { } }
                    res.Outcome = ParkOutcome.Failed; res.Why = rel + ": " + ex.Message;
                    break;
                }
            }
            try { RemoveEmptyDirs(Path.Combine(gameDir, "BepInEx")); } catch (Exception) { }
            if (!Save(sets, new List<ParkedMod>())) { res.Outcome = ParkOutcome.Failed; res.Why = (res.Why == null ? "" : res.Why + "; ") + "record not saved"; }
            if (res.Outcome != ParkOutcome.Failed) res.Outcome = ParkOutcome.Done;
            return res;
        }

        private static void RemoveEmptyDirs(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var d in Directory.GetDirectories(dir)) RemoveEmptyDirs(d);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }

        // ---------------------------------------------------------------- put back

        /// <summary>
        /// Puts parked files back (NyangTalk's connector should be removed first). A file whose parked copy no longer matches
        /// its hash, or whose place is taken, stays parked and is reported.
        /// </summary>
        public ParkResult RestoreAll(string gameDir)
        {
            var res = new ParkResult();
            if (GameRunning()) { res.Outcome = ParkOutcome.GameRunning; return res; }
            var sets = Sets();
            if (RecordBroken) { res.Outcome = ParkOutcome.Failed; res.Why = "move record unreadable"; return res; }
            var oldOk = OldMoves().Count == 0 || RestoreOld(gameDir, res);
            if (sets.Count == 0 && res.Moved.Count == 0) { res.Outcome = oldOk ? ParkOutcome.NothingToPark : ParkOutcome.Failed; return res; }
            foreach (var set in sets.OrderByDescending(s => s.TimeUtc))
            {
                var left = new List<ParkedFile>();
                foreach (var f in set.Files)
                {
                    var parked = Path.Combine(set.Folder, f.Rel);
                    var orig = Path.Combine(gameDir, f.Rel);
                    try
                    {
                        if (!Inside(gameDir, f.Rel) || !Inside(set.Folder, f.Rel)) { res.Skipped.Add(f.Rel + " (bad path in the record)"); left.Add(f); continue; }
                        if (!File.Exists(parked)) { res.Skipped.Add(f.Rel + " (parked copy missing: " + set.Folder + ")"); left.Add(f); continue; }
                        if (Sha256(parked) != f.Sha256) { res.Skipped.Add(f.Rel + " (parked copy changed)"); left.Add(f); continue; }
                        if (File.Exists(orig)) { res.Skipped.Add(f.Rel + " (place taken)"); left.Add(f); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(orig));
                        bool copied = false;
                        try
                        {
                            File.Copy(parked, orig, false);
                            copied = true;
                            if (Sha256(orig) != f.Sha256) throw new IOException("copy differs");
                        }
                        catch (Exception) { if (copied || File.Exists(orig)) { try { if (Sha256(orig) != f.Sha256 || !copied) File.Delete(orig); } catch (Exception) { } } throw; }
                        File.Delete(parked);
                        res.Moved.Add(f.Rel);
                    }
                    catch (Exception ex) { res.Why = f.Rel + ": " + ex.Message; if (!res.Moved.Contains(f.Rel)) left.Add(f); }
                }
                set.Files.Clear(); set.Files.AddRange(left);
                if (left.Count == 0) { try { if (Directory.Exists(set.Folder)) { RemoveEmptyDirs(set.Folder); } } catch (Exception) { } }
            }
            res.Outcome = ParkOutcome.Done;
            if (!Save(sets, OldMoves())) res.Why = (res.Why == null ? "" : res.Why + "; ") + "record not saved";
            res.Outcome = res.Why == null && oldOk && sets.All(s => s.Files.Count == 0) ? ParkOutcome.Done : ParkOutcome.Failed;
            if (res.Outcome == ParkOutcome.Failed) res.Folder = sets.Where(s => s.Files.Count > 0).Select(s => s.Folder).FirstOrDefault();
            return res;
        }

        /// <summary>Undoes the first version's moves (BepInEx\NyangTalk-parked → original place).</summary>
        private bool RestoreOld(string gameDir, ParkResult res)
        {
            var left = new List<ParkedMod>();
            foreach (var m in OldMoves())
            {
                var src = Path.Combine(gameDir, m.To);
                var dst = Path.Combine(gameDir, m.From);
                try
                {
                    if (!File.Exists(src) && !Directory.Exists(src)) { res.Skipped.Add(m.From + " (gone from " + m.To + ")"); continue; }
                    if (File.Exists(dst) || Directory.Exists(dst)) { res.Skipped.Add(m.From + " (place taken)"); left.Add(m); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst);
                    res.Moved.Add(m.From);
                }
                catch (IOException ex) { res.Why = m.From + ": " + ex.Message; left.Add(m); }
                catch (UnauthorizedAccessException ex) { res.Why = m.From + ": " + ex.Message; left.Add(m); }
            }
            bool saved = Save(Sets(), left);
            if (!saved) res.Why = (res.Why == null ? "" : res.Why + "; ") + "record not saved";
            try { RemoveEmptyDirs(Path.Combine(gameDir, OldParkDir)); } catch (Exception) { }
            return left.Count == 0 && saved;
        }
    }
}
