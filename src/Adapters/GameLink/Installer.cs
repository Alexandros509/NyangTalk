using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.Application;
using NyangTalk.Domain.GameLink;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>Files to put into the game folder. Embedded in the exe, optionally extended by a "payload" folder next to it.</summary>
    public sealed class Payload
    {
        public readonly List<PayloadFile> Files = new List<PayloadFile>();
        public readonly Dictionary<string, byte[]> Bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        public string Version;

        public void Add(string rel, byte[] data, bool backupOriginal)
        {
            rel = rel.Replace('/', '\\');
            Bytes[rel] = data;
            Files.RemoveAll(f => string.Equals(f.RelativePath, rel, StringComparison.OrdinalIgnoreCase));
            Files.Add(new PayloadFile { RelativePath = rel, Sha256 = Hash(data), Size = data.Length, BackupOriginal = backupOriginal });
        }

        public static string Hash(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public bool HasLoader { get { return Files.Any(f => f.RelativePath.StartsWith("BepInEx\\core\\", StringComparison.OrdinalIgnoreCase)); } }

        private static bool IsBackupableMetadata(string rel)
        {
            var norm = (rel ?? "").Replace('/', '\\');
            return string.Equals(norm, "changelog.txt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(norm, ".doorstop_version", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Manifest resources named "payload/&lt;relative path&gt;" plus files under &lt;folder&gt;.</summary>
        public static Payload Load(Assembly asm, string extraFolder, string version)
        {
            var p = new Payload { Version = version };
            if (asm != null)
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("payload/", StringComparison.Ordinal)))
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        var rel = name.Substring("payload/".Length);
                        p.Add(rel, Prepare(rel, ms.ToArray()), IsBackupableMetadata(rel));
                    }
            if (extraFolder != null && Directory.Exists(extraFolder))
                foreach (var f in Directory.GetFiles(extraFolder, "*", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(extraFolder.Length).TrimStart('\\', '/');
                    p.Add(rel, Prepare(rel, File.ReadAllBytes(f)), IsBackupableMetadata(rel));
                }
            return p;
        }

        public const string DoorstopConfig = "doorstop_config.ini";

        /// <summary>The installed form of a payload file; only the doorstop configuration differs from the official file.</summary>
        public static byte[] Prepare(string rel, byte[] data)
        {
            return string.Equals((rel ?? "").Replace('/', '\\'), DoorstopConfig, StringComparison.OrdinalIgnoreCase) ? IgnoreDisableSwitch(data) : data;
        }

        /// <summary>
        /// Sets <c>ignore_disable_switch = true</c> (2026-11-26). Doorstop puts DOORSTOP_DISABLE=TRUE into the game's
        /// environment; when the game itself starts Steam, Steam inherits it and every later game start from that Steam
        /// skips BepInEx, so the connector never loads. Other lines and line endings stay as they are.
        /// </summary>
        public static byte[] IgnoreDisableSwitch(byte[] ini)
        {
            var text = new System.Text.UTF8Encoding(false).GetString(ini);
            var lines = text.Split('\n');
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimEnd('\r').Trim();
                if (t.StartsWith("#") || t.StartsWith(";")) continue;
                int eq = t.IndexOf('=');
                if (eq < 0 || !string.Equals(t.Substring(0, eq).Trim(), "ignore_disable_switch", StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = "ignore_disable_switch = true" + (lines[i].EndsWith("\r") ? "\r" : "");
                found = true;
            }
            if (!found) return ini;   // unknown layout: leave the official file untouched rather than guess a section
            return new System.Text.UTF8Encoding(false).GetBytes(string.Join("\n", lines));
        }
    }

    public enum InstallOutcome { Done, NothingToDo, Blocked, NeedsElevation, PrecheckFailed, LoaderMissing, Failed, RecordUnreadable }

    public sealed class InstallResult
    {
        public InstallOutcome Outcome;
        public PlanBlock Block;
        public List<string> Reasons = new List<string>();
        public List<string> Changed = new List<string>();
        public CheckReport Precheck;
        public bool RolledBack;
        /// <summary>The user's (or an earlier mod's) BepInEx was kept and only NyangTalk's plugin was added.</summary>
        public bool KeptForeignLoader;
        public override string ToString() { return Outcome + (Block != PlanBlock.None ? "/" + Block : "") + " " + string.Join(", ", Reasons); }
    }

    /// <summary>
    /// Executes install / uninstall / disable plans (40-SPEC 5장). The install record and backups live in the app data
    /// folder, never in the game folder. Only files in the record are ever deleted or renamed.
    /// </summary>
    public sealed class Installer
    {
        public const string RecordDoc = "install-record";
        public const string PluginDir = @"BepInEx\plugins\NyangTalk";
        public const string PluginDll = PluginDir + @"\NyangTalk.GameLink.dll";
        public const string DoorstopEntry = "winhttp.dll";
        private readonly IDocumentStore _docs;
        private readonly string _backupRoot;
        public Func<bool> IsGameRunning = () => Process.GetProcessesByName("BongoCat").Length > 0;
        /// <summary>Pre-install mixing check (40-SPEC 1.1 3번); returns the report for the configuration after install.</summary>
        public Func<string, CheckReport> Precheck = gameDir => null;

        public Installer(IDocumentStore docs, string appDataRoot)
        {
            _docs = docs;
            _backupRoot = appDataRoot;
        }

        // ---------------------------------------------------------------- record

        public InstallRecord LoadRecord()
        {
            string broken;
            var d = _docs.Load(RecordDoc, out broken);
            // a damaged record stays a stop sign until the user resolves it (the .broken copy is removed by hand)
            if (d == null && (broken != null || _docs.Exists(RecordDoc) || _docs.HasBrokenCopy(RecordDoc)))
                throw new InvalidDataException("install record unreadable" + (broken != null ? ": kept as " + broken : ""));
            if (d == null) return null;
            var r = new InstallRecord { ComponentVersion = Json.Str(d, "component_version") };
            foreach (var o in Json.List(d, "files") ?? new List<object>())
            {
                var f = Json.Obj(o);
                r.Files.Add(new InstalledFile { RelativePath = Json.Str(f, "path"), InstalledSha256 = Json.Str(f, "sha256"), OriginalSha256 = Json.Str(f, "original_sha256"), BackupPath = Json.Str(f, "backup") });
            }
            foreach (var o in Json.List(d, "created_dirs") ?? new List<object>()) if (o is string) r.CreatedDirs.Add((string)o);
            return r;
        }

        private void SaveRecord(InstallRecord r, string gameDir)
        {
            var d = new Dictionary<string, object>
            {
                { "format", "1.0.0" }, { "game_dir", gameDir }, { "component_version", r.ComponentVersion },
                { "created_dirs", r.CreatedDirs.Select(x => (object)x).ToList() },
                { "files", r.Files.Select(f => (object)new Dictionary<string, object> { { "path", f.RelativePath }, { "sha256", f.InstalledSha256 }, { "original_sha256", f.OriginalSha256 }, { "backup", f.BackupPath } }).ToList() }
            };
            if (!_docs.Save(RecordDoc, d)) throw new IOException("install record could not be saved");
        }

        // ---------------------------------------------------------------- folder state

        /// <summary>Reads hashes of the files that matter and looks for other loaders/plugins. Names only; no file is opened except to hash ours.</summary>
        public GameFolderState ReadState(string gameDir, IEnumerable<string> paths, InstallRecord record)
        {
            var st = new GameFolderState { GameRunning = IsGameRunning() };
            foreach (var rel in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var candidate in new[] { rel, rel + InstallPlanner.DisabledSuffix })
                {
                    var full = Path.Combine(gameDir, candidate);
                    if (File.Exists(full)) st.Hashes[candidate] = GameCheck.Sha256File(full);
                }
            }
            Func<string, bool> ours = rel => record != null && record.Find(rel) != null;
            if (Directory.Exists(Path.Combine(gameDir, "MelonLoader"))) st.ForeignMods.Add("MelonLoader");
            foreach (var proxy in new[] { "version.dll", "winmm.dll", "dinput8.dll", "dxgi.dll" })
                if (File.Exists(Path.Combine(gameDir, proxy)) && !ours(proxy)) st.ForeignMods.Add(proxy);
            var plugins = Path.Combine(gameDir, "BepInEx", "plugins");
            if (Directory.Exists(plugins))
                foreach (var e in Directory.GetFileSystemEntries(plugins))
                    if (!string.Equals(Path.GetFileName(e), "NyangTalk", StringComparison.OrdinalIgnoreCase)) st.ForeignMods.Add(@"BepInEx\plugins\" + Path.GetFileName(e));
            var patchers = Path.Combine(gameDir, "BepInEx", "patchers");
            if (Directory.Exists(patchers))
                foreach (var e in Directory.GetFileSystemEntries(patchers)) st.ForeignMods.Add(@"BepInEx\patchers\" + Path.GetFileName(e));
            var core = Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll");
            if (File.Exists(core) && !ours(@"BepInEx\core\BepInEx.dll"))
            {
                var v = FileVersionInfo.GetVersionInfo(core);
                if (v.FileMajorPart != 5) st.ForeignMods.Add("BepInEx " + v.FileVersion + " (not 5.x)");
            }
            return st;
        }

        public static bool LoaderPresent(string gameDir) { return File.Exists(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll")); }

        /// <summary>True when NyangTalk's own plugin DLL (or its disabled copy) is present in the game folder.</summary>
        public static bool PluginPresent(string gameDir)
        {
            return !string.IsNullOrEmpty(gameDir) && (File.Exists(Path.Combine(gameDir, PluginDll)) || File.Exists(Path.Combine(gameDir, PluginDll + InstallPlanner.DisabledSuffix)));
        }

        /// <summary>True when NyangTalk's connector is installed (an earlier mod's BepInEx alone without our plugin is not installed).</summary>
        public bool IsInstalled(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return false;
            if (PluginPresent(gameDir)) return true;
            try { var r = LoadRecord(); return r != null && r.Files.Count > 0; } catch (InvalidDataException) { return false; }
        }

        /// <summary>A BepInEx that NyangTalk did not install is in the game folder (NyangTalk then runs on top of it).</summary>
        public bool ForeignLoaderKept(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir)) return false;
            InstallRecord record;
            try { record = LoadRecord(); } catch (InvalidDataException) { return false; }
            return LoaderPresent(gameDir) && (record == null || record.Find(@"BepInEx\core\BepInEx.dll") == null);
        }

        // ---------------------------------------------------------------- operations

        public InstallResult Install(string gameDir, Payload payload)
        {
            try { return InstallCore(gameDir, payload); }
            catch (InvalidDataException ex) { return new InstallResult { Outcome = InstallOutcome.RecordUnreadable, Reasons = { ex.Message } }; }
        }

        private InstallResult InstallCore(string gameDir, Payload payload)
        {
            var res = new InstallResult();
            var record = LoadRecord();
            if (!payload.HasLoader && !LoaderPresent(gameDir) && (record == null || record.Find(@"BepInEx\core\BepInEx.dll") == null))
            {
                res.Outcome = InstallOutcome.LoaderMissing; res.Reasons.Add("BepInEx 5"); return res;
            }
            if (payload.HasLoader && LoaderPresent(gameDir) && (record == null || record.Find(@"BepInEx\core\BepInEx.dll") == null))
            {
                // BepInEx 5 already installed by the user: keep theirs, add only our plugin files
                var only = new Payload { Version = payload.Version };
                foreach (var f in payload.Files.Where(x => x.RelativePath.StartsWith(PluginDir + @"\", StringComparison.OrdinalIgnoreCase)))
                    only.Add(f.RelativePath, payload.Bytes[f.RelativePath], f.BackupOriginal);
                payload = only;
                res.Reasons.Add("existing BepInEx kept");
                res.KeptForeignLoader = true;
            }
            var paths = payload.Files.Select(f => f.RelativePath).Concat(record == null ? new string[0] : record.Files.Select(f => f.RelativePath));
            var state = ReadState(gameDir, paths, record);
            var plan = InstallPlanner.PlanInstall(payload.Files, payload.Version, record, state);
            if (plan.Block != PlanBlock.None) { res.Outcome = InstallOutcome.Blocked; res.Block = plan.Block; res.Reasons = plan.Reasons; return res; }
            if (plan.IsNoop)
            {
                if (record == null || record.ComponentVersion != payload.Version) SaveRecord(plan.NewRecord, gameDir);
                res.Outcome = InstallOutcome.NothingToDo; return res;
            }
            var pre = Precheck(gameDir);
            res.Precheck = pre;
            if (pre != null && pre.Mix.Count > 0) { res.Outcome = InstallOutcome.PrecheckFailed; res.Reasons = pre.Mix.Select(m => m.ToString()).ToList(); return res; }
            return Execute(gameDir, plan, payload, res);
        }

        public InstallResult Uninstall(string gameDir)
        {
            InstallRecord record;
            try { record = LoadRecord(); }
            catch (InvalidDataException ex) { return new InstallResult { Outcome = InstallOutcome.RecordUnreadable, Reasons = { ex.Message } }; }
            var res = new InstallResult();
            var state = ReadState(gameDir, record == null ? new string[0] : record.Files.Select(f => f.RelativePath), record);
            state.ForeignMods.Clear(); // removing our own files is fine even if other mods exist
            var plan = InstallPlanner.PlanUninstall(record, state);
            if (plan.Block != PlanBlock.None) { res.Outcome = plan.Block == PlanBlock.NotInstalled ? InstallOutcome.NothingToDo : InstallOutcome.Blocked; res.Block = plan.Block; res.Reasons = plan.Reasons; return res; }
            return Execute(gameDir, plan, null, res);
        }

        /// <summary>Entry to rename for "잠시 끄기": the loader entry if we installed it, otherwise our plugin DLL.</summary>
        public string DisableEntry(InstallRecord record)
        {
            return record != null && record.Find(DoorstopEntry) != null ? DoorstopEntry : PluginDll;
        }

        public InstallResult SetEnabled(string gameDir, bool enabled)
        {
            InstallRecord record;
            try { record = LoadRecord(); }
            catch (InvalidDataException ex) { return new InstallResult { Outcome = InstallOutcome.RecordUnreadable, Reasons = { ex.Message } }; }
            var res = new InstallResult();
            var entry = DisableEntry(record);
            var state = ReadState(gameDir, new[] { entry }, record);
            var plan = enabled ? InstallPlanner.PlanEnable(entry, record, state) : InstallPlanner.PlanDisable(entry, record, state);
            if (plan.Block != PlanBlock.None) { res.Outcome = InstallOutcome.Blocked; res.Block = plan.Block; return res; }
            if (plan.Actions.Count == 0) { res.Outcome = InstallOutcome.NothingToDo; return res; }
            return Execute(gameDir, plan, null, res);
        }

        public bool IsDisabled(string gameDir)
        {
            var entry = DisableEntry(LoadRecord());
            return File.Exists(Path.Combine(gameDir, entry + InstallPlanner.DisabledSuffix)) && !File.Exists(Path.Combine(gameDir, entry));
        }

        public List<string> Drift(string gameDir)
        {
            var record = LoadRecord();
            if (record == null) return new List<string>();
            return InstallPlanner.Drift(record, ReadState(gameDir, record.Files.Select(f => f.RelativePath), record));
        }

        /// <summary>Test hook: throws before the given action index runs (simulates a failure mid-way).</summary>
        public int FailBeforeAction = -1;

        private InstallResult Execute(string gameDir, InstallPlan plan, Payload payload, InstallResult res)
        {
            // Snapshot every path the plan touches so a failure can put the folder back exactly as it was.
            var snapshot = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in plan.Actions)
                foreach (var rel in new[] { a.RelativePath, a.Kind == InstallActionKind.Rename ? a.Target : null })
                {
                    if (rel == null || snapshot.ContainsKey(rel)) continue;
                    var full = Path.Combine(gameDir, rel);
                    snapshot[rel] = File.Exists(full) ? File.ReadAllBytes(full) : null;
                }
            var dirsBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in plan.Actions)
                for (var d = Path.GetDirectoryName(Path.Combine(gameDir, a.RelativePath)); d != null && d.Length > gameDir.Length; d = Path.GetDirectoryName(d))
                    if (Directory.Exists(d)) dirsBefore.Add(d);
            var createdBackups = new List<string>();
            var restoredBackups = new List<string>();
            int index = 0;
            try
            {
                foreach (var a in plan.Actions)
                {
                    if (index++ == FailBeforeAction) throw new IOException("simulated failure (test)");
                    var target = Path.Combine(gameDir, a.RelativePath);
                    switch (a.Kind)
                    {
                        case InstallActionKind.BackupThenCopy:
                            var backup = Path.Combine(_backupRoot, InstallPlanner.BackupName(a.RelativePath));
                            Directory.CreateDirectory(Path.GetDirectoryName(backup));
                            File.Copy(target, backup, true);
                            createdBackups.Add(backup);
                            goto case InstallActionKind.Copy;
                        case InstallActionKind.Copy:
                            NoteCreatedDirs(gameDir, Path.GetDirectoryName(target), plan.NewRecord);
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            var tmp = target + ".nyangtalk-tmp";
                            File.WriteAllBytes(tmp, payload.Bytes[a.RelativePath]);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(tmp, target);
                            break;
                        case InstallActionKind.Delete:
                            if (File.Exists(target)) File.Delete(target);
                            break;
                        case InstallActionKind.RestoreBackup:
                            var src = Path.Combine(_backupRoot, a.Target);
                            if (!File.Exists(src)) throw new IOException("backup missing, original not restored: " + a.RelativePath);
                            File.Copy(src, target, true);
                            restoredBackups.Add(src);   // deleted only after everything succeeded
                            break;
                        case InstallActionKind.Rename:
                            File.Move(target, Path.Combine(gameDir, a.Target));
                            break;
                    }
                    res.Changed.Add(a.ToString());
                }
                foreach (var d in plan.RemoveDirs)
                {
                    var full = Path.Combine(gameDir, d);
                    if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full);
                }
                if (plan.NewRecord != null) SaveRecord(plan.NewRecord, gameDir);
                foreach (var b in restoredBackups) { try { File.Delete(b); } catch (IOException) { } }
                res.Outcome = InstallOutcome.Done;
            }
            catch (Exception ex)
            {
                res.Outcome = ex is UnauthorizedAccessException ? InstallOutcome.NeedsElevation : InstallOutcome.Failed;
                res.Reasons.Add(ex.Message);
                res.RolledBack = Rollback(gameDir, snapshot, dirsBefore, createdBackups, res.Reasons);
                res.Changed.Clear();
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is KeyNotFoundException)) throw;
            }
            return res;
        }

        /// <summary>Puts every touched file back to its snapshot and removes folders the attempt created. Returns true if complete.</summary>
        private static bool Rollback(string gameDir, Dictionary<string, byte[]> snapshot, HashSet<string> dirsBefore, List<string> createdBackups, List<string> reasons)
        {
            bool ok = true;
            foreach (var kv in snapshot)
            {
                var full = Path.Combine(gameDir, kv.Key);
                try
                {
                    var tmp = full + ".nyangtalk-tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                    if (kv.Value == null) { if (File.Exists(full)) File.Delete(full); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllBytes(full, kv.Value); }
                }
                catch (Exception ex) { ok = false; reasons.Add("rollback failed for " + kv.Key + ": " + ex.Message); }
            }
            foreach (var kv in snapshot.OrderByDescending(k => k.Key.Length))
                for (var d = Path.GetDirectoryName(Path.Combine(gameDir, kv.Key)); d != null && d.Length > gameDir.Length; d = Path.GetDirectoryName(d))
                {
                    if (dirsBefore.Contains(d) || !Directory.Exists(d) || Directory.EnumerateFileSystemEntries(d).Any()) break;
                    try { Directory.Delete(d); } catch (IOException) { ok = false; }
                }
            foreach (var b in createdBackups) { try { File.Delete(b); } catch (IOException) { } }
            reasons.Add(ok ? "rolled back: game folder is as before" : "rollback incomplete");
            return ok;
        }

        private static void NoteCreatedDirs(string gameDir, string dir, InstallRecord record)
        {
            if (record == null) return;
            var root = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
            var missing = new List<string>();
            while (dir != null && Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar).Length > root.Length && !Directory.Exists(dir))
            {
                missing.Add(Path.GetFullPath(dir).Substring(root.Length + 1));
                dir = Path.GetDirectoryName(dir);
            }
            foreach (var m in missing) if (!record.CreatedDirs.Contains(m, StringComparer.OrdinalIgnoreCase)) record.CreatedDirs.Add(m);
        }
    }
}
