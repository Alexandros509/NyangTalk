using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NyangTalk.Adapters.GameLink;
using NyangTalk.Adapters.Update;
using NyangTalk.App;
using NyangTalk.App.Platform;

namespace NyangTalk.Tests
{
    /// <summary>95-SPEC 1.1, 1.2, 2.1: one app at a time, a fixed launch path, opening with Bongo Cat (fake registry and processes only).</summary>
    public static class StartupTests
    {
        [Test("95-1.1")]
        public static void OnlyOneAppAndTheSecondBringsTheFirstForward()
        {
            var name = SingleInstance.NameFor(Ctx.NewDir("single"), "app");
            using (var first = SingleInstance.TryAcquire(name, TimeSpan.Zero))
            {
                A.True(first != null, "first start takes the lock");
                var asked = new ManualResetEvent(false);
                first.OnActivate(() => asked.Set());
                bool held = false;
                var probe = new Thread(() => held = SingleInstance.IsHeld(name));   // another thread, as another process would
                probe.Start(); probe.Join();
                A.True(held, "held");
                SingleInstance second = null;
                var t = new Thread(() => second = SingleInstance.TryAcquire(name, TimeSpan.Zero));
                t.Start(); t.Join();
                A.True(second == null, "a second start does not get it");
                A.True(SingleInstance.SignalOther(name), "the second start signals the first");
                A.True(asked.WaitOne(3000), "the first is asked to come to the front");
            }
            bool still = true;
            var probe2 = new Thread(() => still = SingleInstance.IsHeld(name));
            probe2.Start(); probe2.Join();
            A.False(still, "released when the app ends");
            // after "지금 재시작" the new copy waits for the old one instead of signalling it
            var name2 = SingleInstance.NameFor(Ctx.NewDir("single2"), "app");
            var old = SingleInstance.TryAcquire(name2, TimeSpan.Zero);
            SingleInstance next = null;
            var waiter = new Thread(() => next = SingleInstance.TryAcquire(name2, TimeSpan.FromSeconds(5)));
            waiter.Start();
            Thread.Sleep(300);
            old.Dispose();
            waiter.Join();
            A.True(next != null, "the restarted copy gets the lock once the old one ends");
            next.Dispose();
            A.True(SingleInstance.NameFor("C:\\a", "app") != SingleInstance.NameFor("C:\\a-demo", "app"), "demo and real data are apart");
            A.True(SingleInstance.NameFor("C:\\a", "app") != SingleInstance.NameFor("C:\\a", "watcher"), "the watcher has its own lock");
        }

        [Test("95-1.2")]
        public static void FixedPathOpensTheNewestVersion()
        {
            var data = Ctx.NewDir("fixed-path");
            var self = Path.Combine(Ctx.NewDir("downloads"), "NyangTalk.exe");
            File.WriteAllText(self, "v0.1.0 exe");
            string why;
            var fixedExe = FixedLaunchPath.Ensure(data, self, out why);
            A.Eq(Path.Combine(data, "NyangTalk.exe"), fixedExe, "fixed copy in the app data: " + why);
            A.Eq("v0.1.0 exe", File.ReadAllText(fixedExe), "a copy of the running app");
            File.WriteAllText(self, "moved or deleted later");
            A.Eq(fixedExe, FixedLaunchPath.Ensure(data, self, out why), "kept as it is");
            // an update put 0.2.0 into the version folders; starting the fixed (0.1.0) copy hands over to it
            var store = new VersionStore(data);
            Directory.CreateDirectory(Path.GetDirectoryName(store.ExeFor("0.2.0")));
            File.WriteAllText(store.ExeFor("0.2.0"), "v0.2.0 exe");
            var staged = Path.Combine(data, "staged.exe"); File.WriteAllText(staged, "v0.2.0 exe");
            store.Stage("0.2.0", staged, false);
            string started = null;
            var launcher = new Launcher(store) { Start = (exe, a) => { started = exe; store.ConfirmStarted("0.2.0"); return null; }, Sleep = ms => { } };
            string rb;
            launcher.OnStart("0.1.0", false, out rb);
            A.Eq(store.ExeFor("0.2.0"), started, "the fixed path opened the new version");
        }

        [Test("95-2.1")]
        public static void StartWithGameRegistersAndCleansUp()
        {
            using (var rig = new UiRig("autostart", 3))
            {
                var run = (FakeRunKey)Shell.Link.RunKey;
                string watcherStarted = null;
                Shell.Link.StartWatcher = exe => watcherStarted = exe;
                A.Eq("false", NyangTalk.Domain.Preferences.Settings.Def("link.startWithGame").Default, "off by default");
                A.Eq(0, run.Values.Count, "nothing registered by default");

                A.True(Shell.Link.SetStartWithGame(true), "turned on");
                var fixedExe = FixedLaunchPath.For(rig.Host.DataRoot);
                A.Eq(AutoStart.CommandFor(fixedExe), run.Get(AutoStart.ValueName), "one entry, the fixed path with --watch-game");
                A.Eq(1, run.Values.Count, "only that one value");
                A.Eq(fixedExe, watcherStarted, "the watcher starts right away");
                A.True(Shell.Link.SetStartWithGame(false), "turned off");
                A.Eq(0, run.Values.Count, "entry removed");

                run.Fail = true;
                A.False(Shell.Link.SetStartWithGame(true), "a failed registration keeps it off");
                A.True(rig.Form.NoticeBand.Visible && rig.Form.NoticeLabel.Text.Contains("denied"), "and says why: " + rig.Form.NoticeLabel.Text);
                A.True(rig.Host.Prefs.L("note.link.startWithGame").Contains("지우기 전에"), "settings tell to turn it off before deleting the app");
            }
        }

        [Test("95-2.1")]
        public static void WatcherOpensOnGameStartOnly()
        {
            var run = new FakeRunKey();
            var fixedExe = @"C:\fake\NyangTalk.exe";
            new AutoStart(run).TurnOn(fixedExe, out _ignored);
            bool fixedThere = true;
            var game = @"C:\Games\BongoCat";
            var running = new List<string> { Path.Combine(game, "BongoCat.exe") };   // the game already runs at sign-in
            int opened = 0;
            var w = new GameWatcher(run, fixedExe) { GameDir = () => game, RunningGameExes = () => running.ToList(), FileExists = p => fixedThere, OpenApp = () => opened++ };
            w.Tick();
            A.Eq(0, opened, "a game already running when the watcher starts does not open the app");
            running.Clear(); w.Tick();
            running.Add(Path.Combine(game, "BongoCat.exe")); w.Tick();
            A.Eq(1, opened, "the game started: open once");
            w.Tick(); w.Tick();
            A.Eq(1, opened, "the user closed the app during the same game: not opened again");
            running.Add(@"D:\Other\BongoCat.exe"); w.Tick();
            A.Eq(1, opened, "another copy of the game elsewhere does not count");
            running.Clear(); w.Tick();
            running.Add(Path.Combine(game, "BongoCat.exe")); w.Tick();
            A.Eq(2, opened, "game restarted: open again");

            new AutoStart(run).TurnOff(out _ignored);
            w.Tick();
            A.True(w.Ended && w.EndReason == "turned off", "turned off in the settings: the watcher ends");

            var run2 = new FakeRunKey();
            new AutoStart(run2).TurnOn(fixedExe, out _ignored);
            var w2 = new GameWatcher(run2, fixedExe) { GameDir = () => game, FileExists = p => false };
            w2.Tick();
            A.True(w2.Ended && w2.EndReason == "app removed", "the app folder is gone: ends");
            A.Eq(0, run2.Values.Count, "and removes its own start-up entry");
        }

        private static string _ignored;
    }
}
