using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NyangTalk.Adapters.GameLink;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.Domain.GameLink;

namespace NyangTalk.Tests
{
    /// <summary>40-SPEC 6장 1–8 on fake game folders. The real game folder is never touched.</summary>
    public static class InstallerTests
    {
        private sealed class Env
        {
            public string Game, Data;
            public Installer Inst;
            public bool Running;
        }

        private static Env New(string tag, bool withLoader = true)
        {
            var e = new Env { Game = Ctx.NewDir("game-" + tag), Data = Ctx.NewDir("data-" + tag) };
            File.WriteAllText(Path.Combine(e.Game, "BongoCat.exe"), "fake exe");
            Directory.CreateDirectory(Path.Combine(e.Game, "BongoCat_Data", "Managed"));
            File.WriteAllText(Path.Combine(e.Game, "BongoCat_Data", "Managed", "Assembly-CSharp.dll"), "fake");
            e.Inst = new Installer(new JsonDocumentStore(Path.Combine(e.Data, "docs")), e.Data);
            e.Inst.IsGameRunning = () => e.Running;
            return e;
        }

        private static Payload Pay(string version, params string[] files)
        {
            var p = new Payload { Version = version };
            foreach (var f in files) p.Add(f, Encoding.UTF8.GetBytes("content of " + f + " v" + version), false);
            return p;
        }

        private static Payload FullPayload(string version)
        {
            // loader files included in the payload (what a build with BepInEx bundled would carry)
            return Pay(version, @"BepInEx\core\BepInEx.dll", "winhttp.dll", "doorstop_config.ini", Installer.PluginDll, Installer.PluginDir + @"\contract.json");
        }

        private static Dictionary<string, string> Snapshot(string dir)
        {
            return Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(p => p.Substring(dir.Length), p => File.Exists(p) ? GameCheck.Sha256File(p) : "<dir>");
        }

        private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(kv => b.ContainsKey(kv.Key) && b[kv.Key] == kv.Value);
        }

        [Test("40-7.1")]
        public static void InstallWritesFilesAndRecord()
        {
            var e = New("i1");
            var p = FullPayload("0.1.0");
            var r = e.Inst.Install(e.Game, p);
            A.Eq(InstallOutcome.Done, r.Outcome, "installed: " + r);
            foreach (var f in p.Files) A.Eq(f.Sha256, GameCheck.Sha256File(Path.Combine(e.Game, f.RelativePath)), "hash " + f.RelativePath);
            var rec = e.Inst.LoadRecord();
            A.Eq(p.Files.Count, rec.Files.Count, "record lists every file");
            A.Eq("0.1.0", rec.ComponentVersion, "version recorded");
            A.False(Directory.GetFiles(e.Game, "*nyangtalk*", SearchOption.AllDirectories).Any(f => f.EndsWith(".json") && f.Contains("record")), "record not in game folder");
        }

        [Test("40-7.2")]
        public static void ReinstallSameVersionChangesNothing()
        {
            var e = New("i2");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            var before = Snapshot(e.Game);
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(InstallOutcome.NothingToDo, r.Outcome, "no-op");
            A.True(Same(before, Snapshot(e.Game)), "folder unchanged");
        }

        [Test("40-7.3")]
        public static void NewVersionAddsOnlyMissingFiles()
        {
            var e = New("i3");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            var v2 = FullPayload("0.1.0");
            v2.Version = "0.1.1";
            v2.Add(Installer.PluginDir + @"\extra.txt", Encoding.UTF8.GetBytes("new file"), false);
            var r = e.Inst.Install(e.Game, v2);
            A.Eq(InstallOutcome.Done, r.Outcome, "installed");
            A.Eq(1, r.Changed.Count, "only the new file: " + string.Join(", ", r.Changed));
            var rec = e.Inst.LoadRecord();
            A.Eq(6, rec.Files.Count, "record merged");
            A.Eq("0.1.1", rec.ComponentVersion, "version updated");
        }

        [Test("40-7.4")]
        public static void ChangedOwnFileIsNotOverwritten()
        {
            var e = New("i4");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            File.WriteAllText(Path.Combine(e.Game, Installer.PluginDll), "changed by someone else");
            var v2 = FullPayload("0.1.1");
            var r = e.Inst.Install(e.Game, v2);
            A.Eq(InstallOutcome.Blocked, r.Outcome, "blocked");
            A.Eq(PlanBlock.ModifiedByOther, r.Block, "reason");
            A.True(r.Reasons.Any(x => x.Contains("NyangTalk.GameLink.dll")), "names the file");
            A.Eq("changed by someone else", File.ReadAllText(Path.Combine(e.Game, Installer.PluginDll)), "file left alone");
        }

        [Test("40-7.5")]
        public static void OtherLoaderOrPluginStopsInstall()
        {
            var e = New("i5");
            Directory.CreateDirectory(Path.Combine(e.Game, "MelonLoader"));
            var before = Snapshot(e.Game);
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(PlanBlock.ForeignLoader, r.Block, "other loader");
            A.True(Same(before, Snapshot(e.Game)), "nothing written");

            var e2 = New("i5b");
            Directory.CreateDirectory(Path.Combine(e2.Game, "BepInEx", "plugins", "SomeoneElsesMod"));
            var r2 = e2.Inst.Install(e2.Game, FullPayload("0.1.0"));
            A.Eq(PlanBlock.ForeignLoader, r2.Block, "other plugin");
            A.True(r2.Reasons.Any(x => x.Contains("SomeoneElsesMod")), "named");
        }

        [Test("40-7.6")]
        public static void GameRunningAsksForRestart()
        {
            var e = New("i6");
            e.Running = true;
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(PlanBlock.GameRunning, r.Block, "game running → restart button");
            A.False(File.Exists(Path.Combine(e.Game, Installer.PluginDll)), "nothing installed");
        }

        [Test("40-7.7")]
        public static void DisableEnableRestoresHashes()
        {
            var e = New("i7");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            var before = Snapshot(e.Game);
            A.Eq(InstallOutcome.Done, e.Inst.SetEnabled(e.Game, false).Outcome, "disabled");
            A.True(e.Inst.IsDisabled(e.Game), "loader entry renamed");
            A.False(File.Exists(Path.Combine(e.Game, "winhttp.dll")), "entry gone while off");
            A.Eq(InstallOutcome.Done, e.Inst.SetEnabled(e.Game, true).Outcome, "enabled");
            A.True(Same(before, Snapshot(e.Game)), "hashes as before");
        }

        [Test("40-7.8")]
        public static void UninstallRestoresOriginalFolder()
        {
            var e = New("i8");
            File.WriteAllText(Path.Combine(e.Game, "doorstop_config.ini"), "original config of the user");
            var before = Snapshot(e.Game);
            var p = FullPayload("0.1.0");
            p.Files.Single(f => f.RelativePath == "doorstop_config.ini").BackupOriginal = true;
            A.Eq(InstallOutcome.Done, e.Inst.Install(e.Game, p).Outcome, "installed over a backed-up original");
            A.True(File.ReadAllText(Path.Combine(e.Game, "doorstop_config.ini")).StartsWith("content of"), "replaced");
            A.Eq(InstallOutcome.Done, e.Inst.Uninstall(e.Game).Outcome, "uninstalled");
            A.True(Same(before, Snapshot(e.Game)), "folder same as before (original restored, created folders removed): " + string.Join(",", Snapshot(e.Game).Keys));
        }

        [Test("40-6")]
        public static void ForeignFileAtOurPathNotOverwritten()
        {
            var e = New("i9");
            File.WriteAllText(Path.Combine(e.Game, "winhttp.dll"), "someone's proxy");
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(PlanBlock.ForeignFileAtPath, r.Block, "not ours, no backup flag → blocked");
            A.Eq("someone's proxy", File.ReadAllText(Path.Combine(e.Game, "winhttp.dll")), "kept");
        }

        [Test("40-6")]
        public static void FailureMidInstallRollsBack()
        {
            var e = New("i13");
            File.WriteAllText(Path.Combine(e.Game, "doorstop_config.ini"), "user config");
            var before = Snapshot(e.Game);
            var p = FullPayload("0.1.0");
            p.Files.Single(f => f.RelativePath == "doorstop_config.ini").BackupOriginal = true;
            e.Inst.FailBeforeAction = 3;
            var r = e.Inst.Install(e.Game, p);
            A.Eq(InstallOutcome.Failed, r.Outcome, "reported as failed");
            A.True(r.RolledBack, "rolled back: " + string.Join("; ", r.Reasons));
            A.True(Same(before, Snapshot(e.Game)), "game folder exactly as before: " + string.Join(",", Snapshot(e.Game).Keys));
            A.True(e.Inst.LoadRecord() == null, "no record of a failed install");
            A.False(Directory.Exists(Path.Combine(e.Data, "backup")) && Directory.GetFiles(Path.Combine(e.Data, "backup"), "*", SearchOption.AllDirectories).Any(), "backup of the failed attempt removed");
            e.Inst.FailBeforeAction = -1;
            A.Eq(InstallOutcome.Done, e.Inst.Install(e.Game, p).Outcome, "a later attempt works");

            // removal that fails half-way also puts everything back
            var installed = Snapshot(e.Game);
            e.Inst.FailBeforeAction = 2;
            var u = e.Inst.Uninstall(e.Game);
            A.True(u.Outcome == InstallOutcome.Failed && u.RolledBack, "uninstall failure rolled back");
            A.True(Same(installed, Snapshot(e.Game)), "folder as before the removal attempt");
            e.Inst.FailBeforeAction = -1;
            A.Eq(InstallOutcome.Done, e.Inst.Uninstall(e.Game).Outcome, "removal works afterwards");
            A.True(Same(before, Snapshot(e.Game)), "and the original state comes back");
        }

        [Test("40-6")]
        public static void UnreadableRecordStopsOperations()
        {
            var e = New("i12");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            File.WriteAllText(Path.Combine(e.Data, "docs", "install-record.json"), "{ damaged");
            var before = Snapshot(e.Game);
            A.Eq(InstallOutcome.RecordUnreadable, e.Inst.Install(e.Game, FullPayload("0.1.1")).Outcome, "install stops");
            A.Eq(InstallOutcome.RecordUnreadable, e.Inst.Uninstall(e.Game).Outcome, "uninstall stops");
            A.True(Same(before, Snapshot(e.Game)), "game folder untouched");
        }

        [Test("40-6")]
        public static void ExistingBepInExIsKept()
        {
            var e = New("i14");
            Directory.CreateDirectory(Path.Combine(e.Game, "BepInEx", "core"));
            var official = Path.Combine(Ctx.Root, "third_party", "BepInEx", "payload", "BepInEx", "core", "BepInEx.dll");
            File.Copy(official, Path.Combine(e.Game, "BepInEx", "core", "BepInEx.dll"));
            File.WriteAllText(Path.Combine(e.Game, "winhttp.dll"), "user's own doorstop");
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(InstallOutcome.Done, r.Outcome, "installed: " + r);
            A.Eq(GameCheck.Sha256File(official), GameCheck.Sha256File(Path.Combine(e.Game, "BepInEx", "core", "BepInEx.dll")), "their BepInEx untouched");
            A.Eq("user's own doorstop", File.ReadAllText(Path.Combine(e.Game, "winhttp.dll")), "their entry file untouched");
            A.True(e.Inst.LoadRecord().Files.All(f => f.RelativePath.StartsWith(Installer.PluginDir)), "only our plugin files recorded");
        }

        [Test("40-6")]
        public static void LoaderMissingIsReported()
        {
            var e = New("i10");
            var r = e.Inst.Install(e.Game, Pay("0.1.0", Installer.PluginDll));
            A.Eq(InstallOutcome.LoaderMissing, r.Outcome, "plugin-only payload needs BepInEx present");
        }

        [Test("40-6")]
        public static void DriftDetectedAfterGameUpdate()
        {
            var e = New("i11");
            e.Inst.Install(e.Game, FullPayload("0.1.0"));
            File.Delete(Path.Combine(e.Game, "winhttp.dll")); // e.g. Steam "verify files" removed it
            A.True(e.Inst.Drift(e.Game).Contains("winhttp.dll"), "drift found → offer reinstall");
        }

        [Test("40-6,doorstop")]
        public static void InstalledDoorstopConfigIgnoresInheritedDisableSwitch()
        {
            var official = File.ReadAllBytes(Path.Combine(Ctx.Root, "third_party", "BepInEx", "payload", "doorstop_config.ini"));
            var text = Encoding.UTF8.GetString(official);
            A.True(text.Contains("ignore_disable_switch = false"), "the official file keeps its default");
            var installed = Encoding.UTF8.GetString(Payload.Prepare("doorstop_config.ini", official));
            A.True(installed.Contains("ignore_disable_switch = true") && !installed.Contains("ignore_disable_switch = false"), "installed config ignores DOORSTOP_DISABLE");
            var before = text.Split('\n').Where(l => !l.Contains("ignore_disable_switch =")).ToList();
            var after = installed.Split('\n').Where(l => !l.Contains("ignore_disable_switch =")).ToList();
            A.True(before.SequenceEqual(after), "every other line, and its line ending, unchanged");
            A.True(Payload.Prepare("doorstop_config.ini", Payload.Prepare("doorstop_config.ini", official)).SequenceEqual(Payload.Prepare("doorstop_config.ini", official)), "applying twice changes nothing more");
            var comment = Encoding.UTF8.GetBytes("# ignore_disable_switch = false\n[General]\n");
            A.True(Payload.Prepare("doorstop_config.ini", comment).SequenceEqual(comment), "unknown layout or comment only: left as the official file");
            A.True(Payload.Prepare(@"BepInEx\core\BepInEx.dll", official).SequenceEqual(official), "other payload files are never changed");

            // end to end: the installed file is the prepared one and the record matches it (no drift right after install)
            var e = New("i12");
            var p = Pay("0.1.5", @"BepInEx\core\BepInEx.dll", "winhttp.dll", Installer.PluginDll, Installer.PluginDir + @"\contract.json");
            p.Add("doorstop_config.ini", Payload.Prepare("doorstop_config.ini", official), false);
            A.Eq(InstallOutcome.Done, e.Inst.Install(e.Game, p).Outcome, "installed");
            A.True(File.ReadAllText(Path.Combine(e.Game, "doorstop_config.ini")).Contains("ignore_disable_switch = true"), "game folder has the switch on");
            A.Eq(0, e.Inst.Drift(e.Game).Count, "record matches the installed config");
        }

        /// <summary>40-1.1 수락: 설치 전 검사가 혼용 문제를 막고, 고친 구성은 통과 (synthetic libraries named like the real case).</summary>
        [Test("40-1.1,10-11.2(합성),10-11.4(합성)")]
        public static void PrecheckBlocksMixingAndPassesWhenFixed()
        {
            var game = GameCheckTests.FakeGame("mix", "namespace G { public class X { } }");
            var managed = GameCheck.ManagedDir(game);
            // game ships a stripped interface library and a stripped Newtonsoft.Json whose JObject implements it
            Compile(Path.Combine(managed, "System.ComponentModel.Lite.dll"), "namespace System.ComponentModel.Lite { public interface ICustomTypeDescriptorLite { string GetClassName(); } }", null);
            Compile(Path.Combine(managed, "Newtonsoft.Json.dll"), "namespace Newtonsoft.Json.Linq { public class JObject : System.ComponentModel.Lite.ICustomTypeDescriptorLite { public string GetClassName() { return null; } } }", Path.Combine(managed, "System.ComponentModel.Lite.dll"));
            // full-profile folder: the interface gains a member (like the full System.dll), Newtonsoft stays stripped
            var full = Ctx.NewDir("fullprofile");
            Compile(Path.Combine(full, "System.ComponentModel.Lite.dll"), "namespace System.ComponentModel.Lite { public interface ICustomTypeDescriptorLite { string GetClassName(); object GetPropertyOwner(object pd); } }", null);
            var e = New("pre");
            e.Inst.Precheck = g => GameCheck.Run(game, full, "{\"contract_version\":1,\"entries\":[]}", null);
            var r = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(InstallOutcome.PrecheckFailed, r.Outcome, "blocked by the pre-install check");
            A.True(r.Precheck.Mix.Any(m => m.Type == "Newtonsoft.Json.Linq.JObject" && m.Fixable), "JObject flagged as fixable");
            // ship the full Newtonsoft.Json that implements the bigger interface → passes
            Compile(Path.Combine(full, "Newtonsoft.Json.dll"), "namespace Newtonsoft.Json.Linq { public class JObject : System.ComponentModel.Lite.ICustomTypeDescriptorLite { public string GetClassName() { return null; } public object GetPropertyOwner(object pd) { return null; } } }", Path.Combine(full, "System.ComponentModel.Lite.dll"));
            var r2 = e.Inst.Install(e.Game, FullPayload("0.1.0"));
            A.Eq(InstallOutcome.Done, r2.Outcome, "passes with the full library: " + r2);
        }

        private static void Compile(string output, string source, string reference)
        {
            using (var p = new Microsoft.CSharp.CSharpCodeProvider())
            {
                var opt = new System.CodeDom.Compiler.CompilerParameters { OutputAssembly = output, GenerateExecutable = false };
                if (reference != null) opt.ReferencedAssemblies.Add(reference);
                var res = p.CompileAssemblyFromSource(opt, source);
                if (res.Errors.HasErrors) throw new Exception(string.Join("; ", res.Errors.Cast<System.CodeDom.Compiler.CompilerError>().Select(x => x.ErrorText)));
            }
        }
    }
}
