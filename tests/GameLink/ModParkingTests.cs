using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NyangTalk.Adapters.GameLink;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.App;

namespace NyangTalk.Tests
{
    /// <summary>"이전 모드 옮기고 새로 설치 / 되돌리기" on fake game folders (never the real one).</summary>
    public static class ModParkingTests
    {
        /// <summary>A fake game folder with an earlier mod's whole BepInEx setup, its own data included.</summary>
        private static string FakeGameWithEarlierMod()
        {
            var g = Ctx.NewDir("fakegame-earlier");
            // marks it as a game folder, so the app uses this folder and never goes looking for the real game
            File.WriteAllText(Path.Combine(g, "BongoCat.exe"), "fake");
            Directory.CreateDirectory(Path.Combine(g, "BongoCat_Data", "Managed"));
            File.WriteAllText(Path.Combine(g, "BongoCat_Data", "Managed", "Assembly-CSharp.dll"), "fake");
            Action<string, string> put = (rel, text) => { var p = Path.Combine(g, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, text); };
            put("winhttp.dll", "old doorstop");
            put("doorstop_config.ini", "[UnityMono]\r\ndll_search_path_override = BepInEx\\unstripped\r\n");
            put(".doorstop_version", "4.0.0");
            put("changelog.txt", "4 commits since v5.4.23.4\r\n\r\nChangelog (excluding merges):\r\n* Upgrade Doorstop to version 4.5.0\r\n");
            put("preloader_20261119_115621_757.log", "preloader log");
            put("earliermod-install.json", "{\"installed\":true}");
            put(@"BepInEx\core\BepInEx.dll", "old bepinex");
            put(@"BepInEx\unstripped\mscorlib.dll", "old corlib");
            put(@"BepInEx\plugins\OtherChat\Other.dll", "other plugin");
            put(@"BepInEx\plugins\OtherChat\data\chat-history.json", "{\"user\":\"data\"}");
            put(@"BepInEx\config\BepInEx.cfg", "cfg");
            put(@"BepInEx\patchers\Patch.dll", "patcher");
            return g;
        }

        private static Dictionary<string, string> Snapshot(string dir)
        {
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(p => p.Substring(dir.Length), p => ModParking.Sha256(p));
        }

        private static Payload FullPayload()
        {
            var p = new Payload { Version = "0.1.0" };
            foreach (var f in new[] { @"BepInEx\core\BepInEx.dll", "winhttp.dll", "doorstop_config.ini", @"BepInEx\unstripped\mscorlib.dll", Installer.PluginDll })
                p.Add(f, Encoding.UTF8.GetBytes("nyangtalk " + f), false);
            return p;
        }

        [Test("40-6,40-1.1,요청-이전모드")]
        public static void EarlierSetupMovesOutInstallsFreshAndComesBack()
        {
            var g = FakeGameWithEarlierMod();
            var data = Ctx.NewDir("park-data");
            var before = Snapshot(g);
            var inst = new Installer(new JsonDocumentStore(Path.Combine(data, "docs")), Path.Combine(data, "install"));
            inst.IsGameRunning = () => false;   // never asks about the real game
            var park = new ModParking(new JsonDocumentStore(Path.Combine(data, "docs")), Path.Combine(data, "parked-mods"));
            park.Owned = () => { var r = inst.LoadRecord(); return r == null ? Enumerable.Empty<string>() : r.Files.Select(f => f.RelativePath).ToList(); };

            A.True(inst.ForeignLoaderKept(g), "an earlier BepInEx is there");
            A.False(inst.IsInstalled(g), "an earlier BepInEx alone without NyangTalk's plugin is not counted as NyangTalk installed");
            A.Eq(12, park.Foreign(g).Count, "loader files, doorstop settings, Doorstop changelog.txt, preloader_*.log, root *-install.json and the whole BepInEx folder are the earlier mod's");
            A.False(park.Foreign(g).Any(f => f.StartsWith("BongoCat")), "the game's own files are never included");

            park.GameRunning = () => true;
            A.Eq(ParkOutcome.GameRunning, park.ParkAll(g).Outcome, "nothing moves while the game runs");
            park.GameRunning = () => false;

            var moved = park.ParkAll(g);
            A.Eq(ParkOutcome.Done, moved.Outcome, "moved: " + moved.Why);
            A.False(Directory.Exists(Path.Combine(g, "BepInEx")) || File.Exists(Path.Combine(g, "winhttp.dll")) || File.Exists(Path.Combine(g, "doorstop_config.ini")) || File.Exists(Path.Combine(g, "changelog.txt")) || File.Exists(Path.Combine(g, "preloader_20261119_115621_757.log")) || File.Exists(Path.Combine(g, "earliermod-install.json")), "the game folder has no earlier setup left");
            A.True(File.Exists(Path.Combine(g, "BongoCat.exe")), "game files untouched");
            var set = park.Sets().Single();
            A.True(set.Folder.StartsWith(Path.Combine(data, "parked-mods")), "kept outside the game folder, in the app data");
            A.Eq("{\"user\":\"data\"}", File.ReadAllText(Path.Combine(set.Folder, @"BepInEx\plugins\OtherChat\data\chat-history.json")), "the earlier mod's own data moved along, not deleted");
            A.True(set.Files.All(f => f.Sha256 == before["\\" + f.Rel]), "every file recorded with its original hash");

            // NyangTalk's own BepInEx bundle goes in fresh (not on top of the earlier one)
            var ins = inst.Install(g, FullPayload());
            A.Eq(InstallOutcome.Done, ins.Outcome, "fresh install: " + ins);
            A.True(inst.IsInstalled(g), "now NyangTalk is installed");
            A.False(ins.KeptForeignLoader, "no earlier loader kept");
            A.Eq("nyangtalk winhttp.dll", File.ReadAllText(Path.Combine(g, "winhttp.dll")), "NyangTalk's loader");
            A.Eq(0, park.Foreign(g).Count, "everything in the game folder is now NyangTalk's");

            // back: remove NyangTalk, put the earlier setup back exactly
            A.Eq(InstallOutcome.Done, inst.Uninstall(g).Outcome, "connector removed first");
            var back = park.RestoreAll(g);
            A.Eq(ParkOutcome.Done, back.Outcome, "restored: " + back.Why + " " + string.Join(", ", back.Skipped));
            var after = Snapshot(g);
            A.True(before.Count == after.Count && before.All(kv => after.ContainsKey(kv.Key) && after[kv.Key] == kv.Value), "the game folder is exactly as before (same files, same hashes)");
            A.False(park.HasParked, "nothing left parked");
        }

        [Test("40-6,요청-이전모드")]
        public static void RestoreNeverOverwritesAndChecksHashes()
        {
            var g = FakeGameWithEarlierMod();
            var data = Ctx.NewDir("park-data2");
            var park = new ModParking(new JsonDocumentStore(Path.Combine(data, "docs")), Path.Combine(data, "parked-mods"));
            A.Eq(ParkOutcome.Done, park.ParkAll(g).Outcome, "parked");
            var set = park.Sets().Single();
            File.WriteAllText(Path.Combine(g, "winhttp.dll"), "something new");                             // a new file took the place
            File.AppendAllText(Path.Combine(set.Folder, @"BepInEx\config\BepInEx.cfg"), "tampered");       // the stored copy changed
            var back = park.RestoreAll(g);
            A.Eq(ParkOutcome.Failed, back.Outcome, "not everything could go back");
            A.Eq("something new", File.ReadAllText(Path.Combine(g, "winhttp.dll")), "the newer file is not overwritten");
            A.False(File.Exists(Path.Combine(g, @"BepInEx\config\BepInEx.cfg")), "a changed stored copy is not put back");
            A.True(back.Skipped.Any(s => s.Contains("winhttp.dll") && s.Contains("taken")) && back.Skipped.Any(s => s.Contains("BepInEx.cfg") && s.Contains("changed")), "both reported: " + string.Join(", ", back.Skipped));
            A.Eq(10, back.Moved.Count, "the rest went back");
            A.Eq(2, park.Sets().Single().Files.Count, "the two stay parked and recorded");
        }

        [Test("요청-이전모드")]
        public static void FirstVersionMovesAreCarriedAlong()
        {
            var g = FakeGameWithEarlierMod();
            var data = Ctx.NewDir("park-data3");
            var docs = new JsonDocumentStore(Path.Combine(data, "docs"));
            // what the first version did: plugins moved to BepInEx\NyangTalk-parked inside the game folder
            Directory.CreateDirectory(Path.Combine(g, @"BepInEx\NyangTalk-parked\plugins"));
            Directory.Move(Path.Combine(g, @"BepInEx\plugins\OtherChat"), Path.Combine(g, @"BepInEx\NyangTalk-parked\plugins\OtherChat"));
            docs.Save(ModParking.Doc, new Dictionary<string, object> { { "moves", new List<object> { new Dictionary<string, object> { { "from", @"BepInEx\plugins\OtherChat" }, { "to", @"BepInEx\NyangTalk-parked\plugins\OtherChat" }, { "time_utc", "2026-11-20T05:00:00.000Z" } } } } });
            var park = new ModParking(docs, Path.Combine(data, "parked-mods"));
            A.True(park.HasParked, "the earlier record is seen");
            var r = park.ParkAll(g);
            A.Eq(ParkOutcome.Done, r.Outcome, "parked: " + r.Why);
            A.Eq(0, park.OldMoves().Count, "the first version's record is resolved");
            A.True(park.Sets().Single().Files.Any(f => f.Rel == @"BepInEx\plugins\OtherChat\Other.dll"), "that plugin went back to its place first and was then parked with the rest");
            A.False(Directory.Exists(Path.Combine(g, "BepInEx")), "nothing left in the game folder");
            A.Eq(ParkOutcome.Done, park.RestoreAll(g).Outcome, "and it all comes back");
            A.True(File.Exists(Path.Combine(g, @"BepInEx\plugins\OtherChat\Other.dll")), "at its original place");
        }

        [Test("10-5.2,요청-이전모드")]
        public static void KeptSetupProblemsPointToTheButton()
        {
            var g = FakeGameWithEarlierMod();
            var data = Ctx.NewDir("park-data4");
            // only the earlier BepInEx itself: other plugins would stop the install earlier ("다른 모드가 있어…")
            Directory.Delete(Path.Combine(g, "BepInEx", "plugins", "OtherChat"), true);
            Directory.Delete(Path.Combine(g, "BepInEx", "patchers"), true);
            File.Copy(Path.Combine(Ctx.Root, "third_party", "BepInEx", "payload", "BepInEx", "core", "BepInEx.dll"), Path.Combine(g, "BepInEx", "core", "BepInEx.dll"), true);   // a real BepInEx 5
            var inst = new Installer(new JsonDocumentStore(Path.Combine(data, "docs")), Path.Combine(data, "install"));
            inst.IsGameRunning = () => false;   // never asks about the real game
            var report = new CheckReport();
            report.Mix.Add(new MixProblem { Assembly = "Newtonsoft.Json", Type = "JObject", Member = "x", RequiredBy = "System", Fixable = true });
            inst.Precheck = dir => report;   // the mixing check run with the existing (earlier) setup
            var r = inst.Install(g, FullPayload());
            A.Eq(InstallOutcome.PrecheckFailed, r.Outcome, "a kept setup with mixing problems is not installed on (" + r + ")");
            A.True(r.KeptForeignLoader, "it was the earlier setup");
            using (var rig = new UiRig("kept-text", 0))
            {
                var text = Shell.Link.Describe(r);
                A.Eq(string.Format(rig.Host.Prefs.L("install.keptSetupMix"), 1), text, "the message names the problem and the button");
                A.True(text.Contains(rig.Host.Prefs.L("mods.park")), "points to the button");
                A.True(rig.Host.Prefs.L("link.typeLoadKept").Contains(rig.Host.Prefs.L("mods.park")), "type-load errors with a kept setup point there too");
            }
        }

        [Test("요청-이전모드,95-0")]
        public static void ParkButtonAsksInTheWindow()
        {
            using (var rig = new UiRig("park-ui", 3))
            {
                var g = FakeGameWithEarlierMod();
                rig.Host.Prefs.Settings.Set("link.gameDir", g);
                var link = Shell.Link;
                if (!string.Equals(link.Location.ForFileOperation(), g, StringComparison.OrdinalIgnoreCase)) throw new AssertFailed("safety: the test must work on the fake folder only, got " + link.Location.ForFileOperation());
                link.Game = new NotRunning();
                link.Parking.GameRunning = () => false;
                using (var f = SettingsWindowTests.Open(rig))
                {
                    f.ShowCategory("link");
                    Func<string, Button> btn = t => f.Area.Content.Controls.OfType<Button>().FirstOrDefault(b => b.Text == t);
                    var park = btn(rig.Host.Prefs.L("mods.park"));
                    var go = btn(rig.Host.Prefs.L("mods.parkGo"));
                    A.True(park != null && park.Visible, "offered when an earlier setup is there");
                    A.True(go != null && !go.Visible, "the doing button is hidden at first");
                    A.False(btn(rig.Host.Prefs.L("mods.restore")).Visible, "nothing to put back yet");
                    A.True(f.Area.Content.Controls.OfType<Label>().Any(l => l.Text == rig.Host.Prefs.L("mods.kept")), "says NyangTalk would run on the earlier BepInEx");
                    int formsBefore = System.Windows.Forms.Application.OpenForms.Count;
                    park.PerformClick();
                    A.Eq(formsBefore, System.Windows.Forms.Application.OpenForms.Count, "no dialog window");
                    A.True(f.Area.Content.Controls.OfType<Label>().Any(l => l.Text.StartsWith(rig.Host.Prefs.L("mods.parkConfirm"))), "the question in plain words, right below");
                    A.True(go.Visible, "and the button that does it");
                    A.True(File.Exists(Path.Combine(g, "winhttp.dll")) && Directory.Exists(Path.Combine(g, "BepInEx")), "nothing moved before the second click");
                }
            }
        }

        [Test("요청-이전모드,검토")]
        public static void ParkingRefusesWhenUnsureAndKeepsWhatItCannotRestore()
        {
            var g = FakeGameWithEarlierMod();
            var data = Ctx.NewDir("park-data5");
            var docs = new JsonDocumentStore(Path.Combine(data, "docs"));
            var park = new ModParking(docs, Path.Combine(data, "parked-mods"));
            var before = Directory.GetFiles(g, "*", SearchOption.AllDirectories).Length;

            park.Owned = () => null;   // the install record could not be read
            var r = park.ParkAll(g);
            A.Eq(ParkOutcome.Failed, r.Outcome, "unknown ownership: nothing moves");
            A.Eq(before, Directory.GetFiles(g, "*", SearchOption.AllDirectories).Length, "every file still in place");

            park.Owned = () => new string[0];
            // what NyangTalk's plugin writes at run time in its own folder is never taken
            Directory.CreateDirectory(Path.Combine(g, @"BepInEx\plugins\NyangTalk"));
            File.WriteAllText(Path.Combine(g, @"BepInEx\plugins\NyangTalk\runtime-state.json"), "ours");
            A.False(park.Foreign(g).Any(f => f.StartsWith(ModParking.OwnPluginDir)), "own plugin folder excluded");

            File.WriteAllText(Path.Combine(data, "docs", ModParking.Doc + ".json"), "{ not json");
            A.Eq(ParkOutcome.Failed, park.ParkAll(g).Outcome, "an unreadable move record is not overwritten");
            A.True(File.Exists(Path.Combine(g, "winhttp.dll")), "nothing moved");
            foreach (var b in Directory.GetFiles(Path.Combine(data, "docs"), ModParking.Doc + "*")) File.Delete(b);

            A.Eq(ParkOutcome.Done, park.ParkAll(g).Outcome, "parked");
            var set = park.Sets().Single();
            File.Delete(Path.Combine(set.Folder, @"BepInEx\core\BepInEx.dll"));   // a stored copy disappeared
            var back = park.RestoreAll(g);
            A.Eq(ParkOutcome.Failed, back.Outcome, "not reported as done");
            A.True(back.Skipped.Any(x => x.Contains("BepInEx.dll") && x.Contains("missing")), "says which one");
            A.True(park.Sets().Single().Files.Any(f => f.Rel == @"BepInEx\core\BepInEx.dll"), "and keeps it in the record");
            A.True(back.Folder != null, "and where the stored files are");
            A.Eq("ours", File.ReadAllText(Path.Combine(g, @"BepInEx\plugins\NyangTalk\runtime-state.json")), "own file untouched throughout");
        }

        private sealed class NotRunning : IGameControl
        {
            public bool IsRunning { get { return false; } }
            public bool RequestClose(TimeSpan wait, out string why) { why = null; return true; }
            public bool LaunchViaSteam(out string why) { why = null; return true; }
            public bool InstallElevated(string gameDir, out string why) { why = null; return true; }
        }
    }
}
