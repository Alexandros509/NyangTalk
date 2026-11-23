using System;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using NyangTalk.Application;

namespace NyangTalk.App
{
    public static class Program
    {
        /// <summary>
        /// Arguments: --demo (fake game data in a separate folder, nothing touches the real game), --data &lt;folder&gt;.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            bool demo = Array.IndexOf(args, "--demo") >= 0;
            string data = null;
            int di = Array.IndexOf(args, "--data");
            if (di >= 0 && di + 1 < args.Length) data = args[di + 1];
            if (data == null) data = demo ? AppHost.DefaultRoot() + "-demo" : AppHost.DefaultRoot();

            // 95-SPEC 2.1: the background watcher (no window, one per data folder)
            if (Array.IndexOf(args, NyangTalk.Adapters.GameLink.AutoStart.WatchArg) >= 0) return WatchGame(data);

            // elevated helper for "연결 다시 설치" (40-SPEC 6장): install only, before any version hand-over, no window
            int ii = Array.IndexOf(args, "--install-connector");
            if (ii >= 0 && ii + 1 < args.Length) return InstallConnector(args[ii + 1], data);

            // Configure WinForms before creating its first HWND; no external .exe.config is needed.
            if (!Platform.DpiStartup.Configured)
            {
                try { return Platform.DpiStartup.Run(args, data); }
                catch (Exception ex)
                {
                    MessageBox.Show("앱을 실행하지 못했습니다.\n" + ex.Message, "NyangTalk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 4;
                }
            }

            // Update hand-over / rollback before anything else (20-SPEC 4장 7–9번).
            var versions = new NyangTalk.Adapters.Update.VersionStore(data);
            string rolledBack = null;
            if (!demo)
            {
                var launcher = new NyangTalk.Adapters.Update.Launcher(versions);
                if (Array.IndexOf(args, "--wait-single") >= 0) launcher.ForwardArgs = " --wait-single";   // restart after an update
                var outcome = launcher.OnStart(AppVersion.Text, Array.IndexOf(args, "--from-launcher") >= 0, out rolledBack);
                if (outcome == NyangTalk.Adapters.Update.Launcher.Outcome.HandedOver) return 0;
            }

            // 95-SPEC 1.1: one app per data folder; a second start brings the first to the front. After "지금 재시작" the new
            // copy waits for the old one to end instead.
            var lockName = NyangTalk.App.Platform.SingleInstance.NameFor(data, "app");
            var single = NyangTalk.App.Platform.SingleInstance.TryAcquire(lockName, Array.IndexOf(args, "--wait-single") >= 0 ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
            if (single == null) { NyangTalk.App.Platform.SingleInstance.SignalOther(lockName); return 0; }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var host = new AppHost(data, new SystemClock(), demo);
            host.AddLog("start NyangTalk " + AppVersion.Text + (demo ? " (demo)" : ""));
            var form = Shell.Create(host);
            single.OnActivate(() => { try { if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke((Action)form.ComeToFront); } catch (InvalidOperationException) { } });
            form.FormClosed += (s, e) => single.Dispose();
            form.Shown += (s, e) =>
            {
                if (Shell.Link != null && host.Demo == null) Shell.Link.StartCheck();
                versions.ConfirmStarted(AppVersion.Text);   // "정상 시작" mark for the launcher
                if (rolledBack != null)
                {
                    new NyangTalk.Adapters.Update.UpdateService(null, host.Docs, AppHost.Version, AppHost.UpdateKeys, data).Block(NyangTalk.Domain.Update.SemVersion.Parse(rolledBack));
                    form.ShowNotice("rolledback", host.Prefs.L("update.rolledBack"));
                }
                if (versions.BrokenCopy != null) form.ShowNotice("versions-broken", string.Format(host.Prefs.L("update.stateBroken"), System.IO.Path.GetFileName(versions.BrokenCopy)));
                if (versions.TakeGamePayloadNotice(AppVersion.Text)) form.ShowNotice("payload", host.Prefs.L("update.gameRestart"));
                form.RestoreDraft();
                form.RunRetention();
            };
            form.FormClosing += (s, e) => form.SaveDraft();
            form.StartTimer();
            System.Windows.Forms.Application.Run(form);
            return 0;
        }

        /// <summary>
        /// The game watcher: checks every 2 s whether the registered game started and then opens the app through the fixed
        /// path (an open app just comes to the front). Ends when its start-up entry is gone or the app was removed.
        /// </summary>
        private static int WatchGame(string data)
        {
            var name = NyangTalk.App.Platform.SingleInstance.NameFor(data, "watcher");
            using (var me = NyangTalk.App.Platform.SingleInstance.TryAcquire(name, TimeSpan.Zero))
            {
                if (me == null) return 0;   // one watcher is enough
                var fixedExe = NyangTalk.Adapters.GameLink.FixedLaunchPath.For(data);
                var docs = new NyangTalk.Adapters.Storage.JsonDocumentStore(System.IO.Path.Combine(data, "data"));
                var w = new NyangTalk.Adapters.GameLink.GameWatcher(new NyangTalk.Adapters.GameLink.RealRunKey(), fixedExe);
                w.GameDir = () =>
                {
                    string broken;
                    var s = docs.Load(NyangTalk.Application.PreferencesService.SettingsDoc, out broken);
                    object v;
                    if (s == null || !s.TryGetValue("values", out v) || !(v is System.Collections.Generic.IDictionary<string, object>)) return null;
                    object dir;
                    return ((System.Collections.Generic.IDictionary<string, object>)v).TryGetValue("link.gameDir", out dir) ? dir as string : null;
                };
                w.RunningGameExes = () =>
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("BongoCat"))
                        using (p) { try { list.Add(p.MainModule.FileName); } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { } }
                    return list;
                };
                w.OpenApp = () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fixedExe) { UseShellExecute = false }); } catch (System.ComponentModel.Win32Exception) { } };
                while (!w.Ended) { w.Tick(); System.Threading.Thread.Sleep(2000); }
                return 0;
            }
        }

        /// <summary>Installs the connector into one game folder and exits: 0 installed or nothing to do, 3 otherwise.</summary>
        private static int InstallConnector(string gameDir, string data)
        {
            try
            {
                var docs = new NyangTalk.Adapters.Storage.JsonDocumentStore(System.IO.Path.Combine(data, "data"));
                var installer = new NyangTalk.Adapters.GameLink.Installer(docs, System.IO.Path.Combine(data, "install"));
                installer.Precheck = g => NyangTalk.Adapters.Metadata.GameCheck.Run(g, LinkServices.DoorstopOverride(g), LinkServices.ContractJson(), null);
                var payload = NyangTalk.Adapters.GameLink.Payload.Load(typeof(Program).Assembly, System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload"), LinkServices.GameLinkVersion);
                var r = installer.Install(gameDir, payload);
                bool ok = r.Outcome == NyangTalk.Adapters.GameLink.InstallOutcome.Done || r.Outcome == NyangTalk.Adapters.GameLink.InstallOutcome.NothingToDo;
                HelperLog(data, "install " + r.Outcome + (r.Reasons.Count > 0 ? " " + string.Join(", ", r.Reasons) : ""));
                return ok ? 0 : 3;
            }
            catch (Exception ex) { HelperLog(data, ex.GetType().Name + ": " + ex.Message); return 4; }
        }

        private static void HelperLog(string data, string line)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(data, "install-helper.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Wires the main window, the settings window and the optional services together.</summary>
    public static class Shell
    {
        public static LinkServices Link;

        public static MainForm Create(AppHost host)
        {
            var form = new MainForm(host);
            var link = new LinkServices(host, form);
            Link = link;
            form.AutomationStatusText = link.AutomationStatus;

            SettingsForm.OpacityPreview = v => form.ApplyOpacity(v);
            form.TickHook = () => { if (host.Demo == null) link.Monitor(); link.PermitTick(); link.UpdateNotices(); };
            SettingsForm open = null;
            form.SettingsOpener = cat =>
            {
                if (open == null || open.IsDisposed) { open = CreateSettings(host, form, link); Placement.Over(open, form); }
                open.Show();
                open.Activate();
                if (cat != null) open.ShowCategory(cat);
            };
            return form;
        }

        /// <summary>The settings window wired to the main window and the link services (not shown yet).</summary>
        public static SettingsForm CreateSettings(AppHost host, MainForm form, LinkServices link)
        {
            var w = new SettingsForm(host.Prefs) { Owner = form };
            w.PageBuilder = link.BuildPage;
            w.StatusLine = link.FeatureStatus;
            w.BeforeChange = (k, v) => link.BeforeChange(w, k, v);
            w.MainWindowSize = () => form.Size;
            w.MainWindowDpi = () => form.DeviceDpiSafe;
            w.SidebarLayout = () => form.CurrentLayout == NyangTalk.Domain.Preferences.LayoutKind.Sidebar;
            w.NotifyPreview = form.PreviewNotification;
            w.EditMyAlias = () =>
            {
                var me = host.Chat.PeopleList().FirstOrDefault(p => p.IsMe);
                if (me == null) { form.ShowNotice("no-me:" + DateTime.UtcNow.Ticks, host.Prefs.L("people.noMe")); return; }
                Dialogs.EditAlias(w, host.Prefs, me.SteamId, me.Name);
            };
            w.OpenAliasList = () => { form.Activate(); if (!form.ParticipantsOpen) form.OpenParticipants(); };
            w.MyAlias = () =>
            {
                var me = host.Chat.PeopleList().FirstOrDefault(p => p.IsMe);
                return me == null ? null : new[] { me.Name, host.Prefs.Aliases.Get(me.SteamId) };
            };
            w.RebuildNow();
            EventHandler onResize = null;
            onResize = (s, e) => { if (w.IsDisposed) { form.Resize -= onResize; return; } if (w.Category == "screen" && w.Page != null) { w.Page.RefreshNotes(); w.Page.Relayout(); } };
            form.Resize += onResize;
            return w;
        }
    }
}
