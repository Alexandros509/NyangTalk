using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Domain.GameLink
{
    public sealed class PayloadFile
    {
        public string RelativePath;  // relative to the game folder, '\' separated
        public string Sha256;
        public long Size;
        /// <summary>If a different original file already exists here, back it up and restore it on uninstall.</summary>
        public bool BackupOriginal;
    }

    public sealed class InstalledFile
    {
        public string RelativePath;
        public string InstalledSha256;
        /// <summary>Hash of the file that existed before we installed (null: no file existed).</summary>
        public string OriginalSha256;
        /// <summary>Backup location of the original file, relative to the app data folder.</summary>
        public string BackupPath;
    }

    public sealed class InstallRecord
    {
        public string ComponentVersion;
        public readonly List<InstalledFile> Files = new List<InstalledFile>();
        /// <summary>Folders that did not exist before we installed; removed on uninstall only if empty.</summary>
        public readonly List<string> CreatedDirs = new List<string>();
        public InstalledFile Find(string rel)
        {
            return Files.FirstOrDefault(f => string.Equals(f.RelativePath, rel, StringComparison.OrdinalIgnoreCase));
        }
    }

    public enum InstallActionKind { Copy, BackupThenCopy, Delete, RestoreBackup, Rename }

    public sealed class InstallAction
    {
        public InstallActionKind Kind;
        public string RelativePath;
        public string Target;          // for Rename
        public PayloadFile Payload;
        public override string ToString() { return Kind + " " + RelativePath + (Target != null ? " -> " + Target : ""); }
    }

    public enum PlanBlock { None, GameRunning, ForeignLoader, ModifiedByOther, ForeignFileAtPath, NotInstalled }

    public sealed class InstallPlan
    {
        public PlanBlock Block;
        public readonly List<string> Reasons = new List<string>();
        public readonly List<InstallAction> Actions = new List<InstallAction>();
        public InstallRecord NewRecord;
        /// <summary>Folders to remove if empty after the actions (uninstall).</summary>
        public readonly List<string> RemoveDirs = new List<string>();
        public bool IsNoop { get { return Block == PlanBlock.None && Actions.Count == 0; } }
    }

    /// <summary>What the game folder looks like right now (read by the adapter).</summary>
    public sealed class GameFolderState
    {
        /// <summary>relative path → sha256 (lower-case hex) for every file that matters, or absent.</summary>
        public readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool GameRunning;
        /// <summary>Signs of another loader or other plugins (found by the adapter), e.g. "MelonLoader", "BepInEx\plugins\X.dll".</summary>
        public readonly List<string> ForeignMods = new List<string>();
        public string Hash(string rel) { string h; return Hashes.TryGetValue(rel, out h) ? h : null; }
    }

    /// <summary>
    /// Pure install/uninstall/disable planning (40-SPEC 5장). Never overwrites a file we did not put there unless the
    /// payload marks it as backup-able, and never touches a file someone else changed after we installed it.
    /// </summary>
    public static class InstallPlanner
    {
        public const string DisabledSuffix = ".nyangtalk-off";

        public static InstallPlan PlanInstall(IList<PayloadFile> payload, string version, InstallRecord record, GameFolderState state)
        {
            var plan = new InstallPlan();
            if (state.GameRunning) { plan.Block = PlanBlock.GameRunning; plan.Reasons.Add("game-running"); return plan; }
            if (state.ForeignMods.Count > 0)
            {
                plan.Block = PlanBlock.ForeignLoader;
                plan.Reasons.AddRange(state.ForeignMods);
                return plan;
            }
            var rec = new InstallRecord { ComponentVersion = version };
            if (record != null) { foreach (var f in record.Files) rec.Files.Add(Clone(f)); rec.CreatedDirs.AddRange(record.CreatedDirs); }

            foreach (var p in payload)
            {
                var current = state.Hash(p.RelativePath);
                var mine = rec.Find(p.RelativePath);
                if (mine != null)
                {
                    if (current == null)
                    {
                        // ours but deleted (e.g. by Steam verify): put it back
                        plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Copy, RelativePath = p.RelativePath, Payload = p });
                        mine.InstalledSha256 = p.Sha256;
                    }
                    else if (Eq(current, p.Sha256)) { mine.InstalledSha256 = p.Sha256; }
                    else if (Eq(current, mine.InstalledSha256))
                    {
                        plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Copy, RelativePath = p.RelativePath, Payload = p });
                        mine.InstalledSha256 = p.Sha256;
                    }
                    else
                    {
                        plan.Block = PlanBlock.ModifiedByOther;
                        plan.Reasons.Add(p.RelativePath);
                    }
                    continue;
                }
                if (current == null)
                {
                    plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Copy, RelativePath = p.RelativePath, Payload = p });
                    rec.Files.Add(new InstalledFile { RelativePath = p.RelativePath, InstalledSha256 = p.Sha256 });
                }
                else if (Eq(current, p.Sha256))
                {
                    // Identical file that is not ours: leave it, do not claim it (uninstall must not delete it).
                }
                else if (p.BackupOriginal)
                {
                    plan.Actions.Add(new InstallAction { Kind = InstallActionKind.BackupThenCopy, RelativePath = p.RelativePath, Payload = p });
                    rec.Files.Add(new InstalledFile { RelativePath = p.RelativePath, InstalledSha256 = p.Sha256, OriginalSha256 = current, BackupPath = BackupName(p.RelativePath) });
                }
                else
                {
                    plan.Block = PlanBlock.ForeignFileAtPath;
                    plan.Reasons.Add(p.RelativePath);
                }
            }

            // Files we installed earlier that the new payload no longer has.
            foreach (var f in rec.Files.ToList())
            {
                if (payload.Any(p => string.Equals(p.RelativePath, f.RelativePath, StringComparison.OrdinalIgnoreCase))) continue;
                var current = state.Hash(f.RelativePath);
                if (current != null && !Eq(current, f.InstalledSha256)) { plan.Block = PlanBlock.ModifiedByOther; plan.Reasons.Add(f.RelativePath); continue; }
                if (current != null) plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Delete, RelativePath = f.RelativePath });
                if (f.BackupPath != null) plan.Actions.Add(new InstallAction { Kind = InstallActionKind.RestoreBackup, RelativePath = f.RelativePath, Target = f.BackupPath });
                rec.Files.Remove(f);
            }

            if (plan.Block != PlanBlock.None) { plan.Actions.Clear(); return plan; }
            plan.NewRecord = rec;
            return plan;
        }

        /// <summary>Complete removal: only files in the record, and only if unchanged; originals come back.</summary>
        public static InstallPlan PlanUninstall(InstallRecord record, GameFolderState state)
        {
            var plan = new InstallPlan();
            if (record == null || record.Files.Count == 0) { plan.Block = PlanBlock.NotInstalled; return plan; }
            if (state.GameRunning) { plan.Block = PlanBlock.GameRunning; plan.Reasons.Add("game-running"); return plan; }
            foreach (var f in record.Files)
            {
                var current = state.Hash(f.RelativePath);
                var disabled = state.Hash(f.RelativePath + DisabledSuffix);
                if (current == null && disabled != null && Eq(disabled, f.InstalledSha256))
                {
                    plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Delete, RelativePath = f.RelativePath + DisabledSuffix });
                }
                else if (current != null && !Eq(current, f.InstalledSha256))
                {
                    plan.Block = PlanBlock.ModifiedByOther; plan.Reasons.Add(f.RelativePath); continue;
                }
                else if (current != null)
                {
                    plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Delete, RelativePath = f.RelativePath });
                }
                if (f.BackupPath != null) plan.Actions.Add(new InstallAction { Kind = InstallActionKind.RestoreBackup, RelativePath = f.RelativePath, Target = f.BackupPath });
            }
            if (plan.Block != PlanBlock.None) { plan.Actions.Clear(); return plan; }
            plan.NewRecord = new InstallRecord();
            plan.RemoveDirs.AddRange(record.CreatedDirs.OrderByDescending(d => d.Length));
            return plan;
        }

        /// <summary>"연결 잠시 끄기": rename only the loader entry file (40-SPEC 5장).</summary>
        public static InstallPlan PlanDisable(string loaderEntry, InstallRecord record, GameFolderState state)
        {
            var plan = new InstallPlan { NewRecord = record };
            if (state.GameRunning) { plan.Block = PlanBlock.GameRunning; return plan; }
            if (record == null || record.Find(loaderEntry) == null) { plan.Block = PlanBlock.NotInstalled; return plan; }
            if (state.Hash(loaderEntry) == null) return plan; // already off
            plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Rename, RelativePath = loaderEntry, Target = loaderEntry + DisabledSuffix });
            return plan;
        }

        public static InstallPlan PlanEnable(string loaderEntry, InstallRecord record, GameFolderState state)
        {
            var plan = new InstallPlan { NewRecord = record };
            if (state.GameRunning) { plan.Block = PlanBlock.GameRunning; return plan; }
            if (record == null || record.Find(loaderEntry) == null) { plan.Block = PlanBlock.NotInstalled; return plan; }
            if (state.Hash(loaderEntry + DisabledSuffix) == null) return plan; // already on
            plan.Actions.Add(new InstallAction { Kind = InstallActionKind.Rename, RelativePath = loaderEntry + DisabledSuffix, Target = loaderEntry });
            return plan;
        }

        /// <summary>Files of ours that differ from the record (e.g. after a Steam update) → offer reinstall.</summary>
        public static List<string> Drift(InstallRecord record, GameFolderState state)
        {
            var r = new List<string>();
            if (record == null) return r;
            foreach (var f in record.Files)
            {
                var cur = state.Hash(f.RelativePath) ?? state.Hash(f.RelativePath + DisabledSuffix);
                if (cur == null || !Eq(cur, f.InstalledSha256)) r.Add(f.RelativePath);
            }
            return r;
        }

        public static string BackupName(string rel) { return "backup\\" + rel; }

        private static bool Eq(string a, string b) { return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        private static InstalledFile Clone(InstalledFile f)
        {
            return new InstalledFile { RelativePath = f.RelativePath, InstalledSha256 = f.InstalledSha256, OriginalSha256 = f.OriginalSha256, BackupPath = f.BackupPath };
        }
    }
}
