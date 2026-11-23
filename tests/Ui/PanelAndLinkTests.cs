using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NyangTalk.Adapters.GameLink;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;
using LinkState = NyangTalk.Domain.GameLink.LinkState;
using LayoutKind = NyangTalk.Domain.Preferences.LayoutKind;

namespace NyangTalk.Tests
{
    /// <summary>2026-11-20 requests: participants side panel, pill colours, window placement, no risk pop-up, button backgrounds,
    /// sidebar in a narrow window, game connection button, reinstall with game restart.</summary>
    public static class PanelAndLinkTests
    {
        private static bool Key(Form f, Keys k)
        {
            var m = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance);
            var msg = new Message();
            return (bool)m.Invoke(f, new object[] { msg, k });
        }

        [Test("요청-1")]
        public static void ParticipantsOpenAsASidePanel()
        {
            using (var rig = new UiRig("people-panel", 10))
            {
                var f = rig.Form;
                rig.Resize(1100, 760); rig.Step();
                var chatWidth = f.Chat.Width;
                f.Faces.PerformClick();
                A.True(f.ParticipantsOpen && f.People.Visible, "the pill opens the panel");
                A.Eq(DockStyle.Right, f.People.Dock, "wide window: docked on the right");
                A.True(f.Chat.Width <= chatWidth - ParticipantsPanel.PanelWidth + 2, "the chat makes room");
                A.True(f.People.Top >= f.Header.Bottom, "below the header");
                A.True(f.Faces.Pressed, "the pill looks pressed while open");
                rig.Save("participants-panel");
                A.Eq(rig.Host.Chat.PeopleList().Count, f.People.List.Items.Count, "everyone listed");
                A.True(f.People.Title.Text.Contains(rig.Host.Chat.PeopleList().Count.ToString()), "title with the count: " + f.People.Title.Text);
                f.Faces.PerformClick();
                A.False(f.ParticipantsOpen, "a second click closes it");
                rig.Resize(510, 790); rig.Step();
                f.OpenParticipants();
                A.True(f.ParticipantsOverlay, "narrow window: over the chat");
                A.True(f.People.Left >= 0 && f.People.Right <= f.Main.ClientSize.Width, "inside the window");
                A.Eq(0, f.Main.Controls.GetChildIndex(f.People), "front-most, over the chat (z-order)");
                A.True(Key(f, Keys.Escape) && !f.ParticipantsOpen, "Esc closes it");
                f.OpenParticipants();
                f.CloseOverlaysOutside(f.Chat.PointToScreen(new Point(5, 5)));
                A.False(f.ParticipantsOpen, "a click outside closes it");
                A.False(System.Windows.Forms.Application.OpenForms.Cast<Form>().Any(w => w.Text == rig.Host.Prefs.L("menu.participants")), "no separate participants window");
            }
        }

        [Test("요청-2")]
        public static void PillFollowsTheThemeAndLooksPressable()
        {
            using (var rig = new UiRig("pill-theme", 5))
            {
                var faces = rig.Form.Faces;
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { false, true })
                    {
                        rig.Host.Prefs.Settings.Set("screen.theme", theme); rig.Host.Prefs.Settings.Set("screen.dark", dark);
                        var th = Theme.Get(theme, dark);
                        var header = faces.Parent.BackColor;
                        var back = faces.PillBack;
                        var line = faces.PillLine;
                        Func<Color, Rgb> rgb = c => new Rgb(c.R, c.G, c.B);
                        A.Eq(RenderStyle.C(th.OtherBubble), back, "participant fill matches the approved header fill");
                        A.Eq(RenderStyle.C(th.Border), line, "participant outline matches the approved border");
                        A.True(ColorMath.Contrast(th.Text, rgb(back)) >= 4.5, theme + "/" + dark + " count text readable");
                    }
            }
        }

        [Test("요청-3")]
        public static void SecondaryWindowsOpenOnTheOwnersMonitor()
        {
            var right = new Rectangle(1920, 0, 1920, 1040);
            var p = Placement.CenterOn(new Rectangle(2880, 100, 510, 790), new Size(720, 680), right);
            A.True(right.Contains(new Rectangle(p, new Size(720, 680))), "on the owner's (second) monitor: " + p);
            A.Eq(2880 + (510 - 720) / 2, p.X, "centred on the owner");
            p = Placement.CenterOn(new Rectangle(3500, 600, 400, 400), new Size(720, 680), right);
            A.True(p.X + 720 <= right.Right && p.Y + 680 <= right.Bottom, "kept inside the work area at the edge: " + p);
            using (var rig = new UiRig("placement", 5))
            {
                rig.Form.OpenSettings("screen");
                var s = System.Windows.Forms.Application.OpenForms.OfType<SettingsForm>().First();
                try
                {
                    A.Eq(FormStartPosition.Manual, s.StartPosition, "placed by the app, not by Windows' default");
                    A.Eq(Screen.FromControl(rig.Form).DeviceName, Screen.FromPoint(new Point(s.Left + s.Width / 2, s.Top + s.Height / 2)).DeviceName, "same monitor as the main window");
                }
                finally { s.Close(); }
                var g = Dialogs.GlobalSearch(rig.Form, rig.Host, h => { });
                try { A.Eq(FormStartPosition.Manual, g.StartPosition, "search window placed by the app"); }
                finally { g.Close(); }
            }
        }

        [Test("요청-4,80-0")]
        public static void AutomationNeedsNoPopUpAndKeepsTheNotice()
        {
            using (var rig = new UiRig("no-popup", 5))
            {
                var link = Shell.Link;
                // a pop-up would block this call; it returns at once and records the consent
                A.True(link.BeforeChange(null, "auto.chest", "true"), "switch on without a dialog");
                A.True(link.BeforeChange(null, "auto.gifts", "true"), "gifts on without a dialog");
                A.True(link.LoadConsent() != null && link.LoadConsent("automation-consent-gifts") != null, "consent records kept");
                var src = File.ReadAllText(Path.Combine(Ctx.Root, "src", "App", "LinkPanels.cs"));
                A.False(src.Contains("MessageBox.Show(owner, L(\"auto.risk"), "no risk message box left");
                using (var f = SettingsWindowTests.Open(rig))
                {
                    f.ShowCategory("auto");
                    var labels = f.Area.Content.Controls.OfType<Label>().ToList();
                    var risk = labels.First(l => l.Text == rig.Host.Prefs.L("auto.risk"));
                    var gifts = labels.First(l => l.Text == rig.Host.Prefs.L("auto.giftsNotice"));
                    A.True(risk.Top < f.Editor("auto.chest").Top && gifts.Top < f.Editor("auto.gifts").Top && gifts.Top > f.Editor("auto.chest").Top, "each notice sits above its switch");
                    A.Eq(f.Pal.Warning.ToArgb(), risk.ForeColor.ToArgb(), "in the warning colour");
                }
            }
        }

        [Test("요청-5")]
        public static void ButtonsSitOnTheirParentsColour()
        {
            using (var rig = new UiRig("blend", 5))
            {
                foreach (var size in new[] { 510, 1100 })
                    foreach (var dark in new[] { false, true })
                    {
                        rig.Resize(size, 760); rig.Host.Prefs.Settings.Set("screen.dark", dark); rig.Step();
                        var accent = RenderStyle.C(rig.Form.CurrentStyle.Theme.Accent);
                        foreach (var b in All(rig.Form).OfType<ButtonBase>().Where(b => b != rig.Form.SendButton))
                        {
                            A.Eq(b.Parent.BackColor.ToArgb(), b.BackColor.ToArgb(), size + "/" + dark + " " + b.AccessibleName + " sits on its parent's colour");
                            A.Eq(Palette.Mix(b.Parent.BackColor, accent, 0.12).ToArgb(), b.FlatAppearance.MouseOverBackColor.ToArgb(), b.AccessibleName + " hovers in the theme colour");
                            A.False(b.FlatAppearance.MouseDownBackColor.IsEmpty, b.AccessibleName + " press colour set");
                        }
                    }
                var pal = Palette.From(Theme.Get("sky", true));
                using (var form = new Form())
                {
                    var btn = new Button(); var box = new TextBox(); var lab = new Label();
                    form.Controls.AddRange(new Control[] { btn, box, lab });
                    form.BackColor = pal.Panel;
                    Dialogs.Paint(form, pal);
                    A.Eq(pal.Panel.ToArgb(), btn.BackColor.ToArgb(), "dialog button themed");
                    A.Eq(pal.Input.ToArgb(), box.BackColor.ToArgb(), "dialog text box themed");
                    A.Eq(pal.Text.ToArgb(), lab.ForeColor.ToArgb(), "dialog label themed");
                }
            }
        }

        private static IEnumerable<Control> All(Control c) { foreach (Control k in c.Controls) { yield return k; foreach (var d in All(k)) yield return d; } }

        [Test("요청-6")]
        public static void NarrowWindowCanOpenTheSidebar()
        {
            using (var rig = new UiRig("narrow-sidebar", 10))
            {
                var f = rig.Form;
                rig.Resize(510, 790); rig.Step();
                A.Eq(LayoutKind.Single, f.CurrentLayout, "single layout");
                A.True(f.ExpandButton.Visible, "the open-sidebar button is there in a narrow window");
                f.ToggleSidebar();
                A.True(f.SidebarOverlay && f.Sidebar.Visible && f.Sidebar.Dock == DockStyle.None, "the sidebar opens over the chat");
                A.True(f.Sidebar.Right <= f.ClientSize.Width && f.Sidebar.Height == f.ClientSize.Height, "inside the window, full height");
                A.Eq(0, f.Controls.GetChildIndex(f.Sidebar), "front-most, over the chat (z-order)");
                A.True(Key(f, Keys.Escape) && !f.SidebarOverlay && !f.Sidebar.Visible, "Esc closes it");
                f.ToggleSidebar();
                f.CloseOverlaysOutside(f.Chat.PointToScreen(new Point(f.Chat.Width - 5, 5)));
                A.False(f.SidebarOverlay, "a click outside closes it");
                rig.Host.Prefs.Settings.Set("screen.layout", "sidebar");
                rig.Resize(640, 700); rig.Step();
                A.True(f.SidebarCollapsed && f.ExpandButton.Visible, "fixed sidebar layout, narrow: collapsed with the open button");
                f.ToggleSidebar();
                A.True(f.SidebarOverlay && f.Sidebar.Visible, "and it still opens");
                Key(f, Keys.Escape);
                HeaderChecks.NoOverlap(f, "narrow sidebar");
            }
        }

        [Test("95-2.6,요청-7")]
        public static void ConnectionProblemsShowOneActionOnTheNoticeLine()
        {
            Func<LinkState, bool, bool, LinkServices.NoticeActionKind> act = LinkServices.ConnectionAction;
            A.Eq(LinkServices.NoticeActionKind.None, act(LinkState.Connected, true, false), "connected: nothing");
            A.Eq(LinkServices.NoticeActionKind.Install, act(LinkState.NotInstalled, false, false), "연결 설치");
            A.Eq(LinkServices.NoticeActionKind.ReinstallRestart, act(LinkState.NeedsReinstall, true, false), "설치하고 게임 재시작 while the game runs");
            A.Eq(LinkServices.NoticeActionKind.Install, act(LinkState.NeedsComponentUpdate, false, false), "plain install when it does not");
            A.Eq(LinkServices.NoticeActionKind.Park, act(LinkState.Connected, true, true), "다른 모드 옮기고 설치 for a problem setup");
            A.Eq(LinkServices.NoticeActionKind.OpenSteam, act(LinkState.Waiting, false, false), "Steam에서 봉고캣 열기");
            A.Eq(LinkServices.NoticeActionKind.Reconnect, act(LinkState.Disconnected, true, false), "다시 연결");
            using (var rig = new UiRig("link-notice", 5))
            {
                Shell.Link.UpdateNotices();
                A.True(rig.Form.Notices.Get(NoticeKind.Connection) == null, "connected: no connection notice");
                A.Eq(0, rig.Form.Header.Controls.OfType<Button>().Count(b => b.Visible && b.Text.Length > 0), "the status line has no button (moved to the notice line)");
            }
            var dir = Ctx.NewDir("ui-nogame");
            var host = new AppHost(dir, new FakeClock(), false);
            var form = Shell.Create(host);
            try
            {
                Shell.Link.RunKey = new FakeRunKey(); Shell.Link.StartWatcher = e => { };
                Shell.Link.Game = new NoGame();
                form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.Opacity = 0;
                form.Show(); form.Tick(); System.Windows.Forms.Application.DoEvents();
                var kind = LinkServices.ConnectionAction(host.Chat.Link, false, false);
                A.True(kind != LinkServices.NoticeActionKind.None, "no game: not connected (" + host.Chat.Link + ")");
                A.True(form.NoticeBand.Visible, "the notice line shows it");
                A.Eq(host.Prefs.L(LinkServices.ActionKey(kind)), form.NoticeAction.Text, "one action for the situation");
                // the update notice waits behind a connection problem
                form.Notices.Set(new NoticeEntry { Key = "u", Kind = NoticeKind.Update, Text = "update", ActionText = "x" });
                A.Eq(NoticeKind.Connection, form.Notices.Current.Kind, "connection first");
                foreach (var w in new[] { 300, 380, 510, 1100 })
                {
                    form.MinimumSize = Size.Empty; form.ClientSize = new Size(w, 700); form.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                    var need = TextRenderer.MeasureText(form.NoticeAction.Text, form.NoticeAction.Font).Width;
                    A.True(form.NoticeAction.Width >= need, w + ": button label not cut (" + form.NoticeAction.Width + " < " + need + ")");
                    A.True(form.NoticeAction.Right <= form.NoticeBand.ClientSize.Width && form.NoticeAction.Bottom <= form.NoticeBand.ClientSize.Height, w + ": button inside the line");
                    A.False(form.NoticeAction.Bounds.IntersectsWith(form.NoticeLabel.Bounds) || form.NoticeAction.Bounds.IntersectsWith(form.NoticeClose.Bounds), w + ": no overlap");
                }
            }
            finally { form.Dispose(); }
        }

        private sealed class NoGame : IGameControl
        {
            public bool IsRunning { get { return false; } }
            public bool RequestClose(TimeSpan wait, out string why) { why = null; return true; }
            public bool LaunchViaSteam(out string why) { why = null; return true; }
            public bool InstallElevated(string gameDir, out string why) { why = null; return true; }
        }

        [Test("95-2.6")]
        public static void NoticeLinePriorityAndDismissal()
        {
            var line = new NoticeLine();
            line.Set(new NoticeEntry { Key = "i", Kind = NoticeKind.Info, Text = "info" });
            line.Set(new NoticeEntry { Key = "h", Kind = NoticeKind.Health, Text = "health" });
            line.Set(new NoticeEntry { Key = "u", Kind = NoticeKind.Update, Text = "update" });
            A.Eq("update", line.Current.Text, "update before health before info");
            line.Set(new NoticeEntry { Key = "c", Kind = NoticeKind.Connection, Text = "conn" });
            A.Eq("conn", line.Current.Text, "connection first");
            line.Dismiss("c");
            A.Eq("update", line.Current.Text, "closed: the next one");
            line.Set(new NoticeEntry { Key = "c", Kind = NoticeKind.Connection, Text = "conn" });
            A.Eq("update", line.Current.Text, "a closed key stays closed");
            line.Clear(NoticeKind.Update);
            A.Eq("health", line.Current.Text, "cleared");
        }

        [Test("95-2.2")]
        public static void UpdateCheckTimingNoticeAndDeferral()
        {
            var t0 = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
            var sched = new UpdateSchedule(t0);
            A.False(sched.Due(t0.AddSeconds(29)), "not before 30 s");
            A.True(sched.Due(t0.AddSeconds(30)), "30 s after start");
            A.False(sched.Due(t0.AddHours(3)), "not again soon (also after a failure)");
            A.True(sched.Due(t0.AddSeconds(30).AddHours(6)), "every 6 hours");
            using (var rig = new UiRig("update-notice", 3))
            {
                var link = Shell.Link;
                link.Game = new NoGame();
                link.Schedule = new UpdateSchedule(DateTime.UtcNow.AddDays(1));   // no real check in this test
                int restarts = 0;
                link.Restart = () => restarts++;
                link.ReadyVersion = "0.2.0";
                rig.Form.Input.Text = "쓰던 글";
                link.UpdateNotices();
                var n = rig.Form.Notices.Get(NoticeKind.Update);
                A.True(n != null && n.Text.Contains("0.2.0") && n.Text.Contains(rig.Host.Prefs.L("update.defer.draft")) && n.ActionText == null, "draft: restart waits and says why: " + (n == null ? "-" : n.Text));
                rig.Form.Input.Text = "";
                link.UpdateNotices();
                n = rig.Form.Notices.Get(NoticeKind.Update);
                A.Eq(rig.Host.Prefs.L("update.restartNow"), n.ActionText, "then 지금 재시작");
                n.Action();
                A.Eq(1, restarts, "restart runs");
                link.Busy = true; link.UpdateNotices();
                A.True(rig.Form.Notices.Get(NoticeKind.Update).ActionText == null, "waits while installing");
                link.Busy = false;
                link.DismissUpdate("0.2.0"); link.UpdateNotices();
                A.True(rig.Form.Notices.Get(NoticeKind.Update) == null, "closed for this version: not shown again");
                link.ReadyVersion = "0.3.0"; link.UpdateNotices();
                A.True(rig.Form.Notices.Get(NoticeKind.Update) != null, "a newer version shows again");
            }
        }

        private sealed class FakeGameControl : IGameControl
        {
            public bool Running, Closes = true, Launches = true, ElevatedOk = true, RecheckFails;
            public List<string> Calls = new List<string>();
            public bool IsRunning { get { return Running; } }
            public bool RequestClose(TimeSpan wait, out string why) { Calls.Add("close"); why = Closes ? null : "timeout:20"; if (Closes) Running = false; return Closes; }
            public bool LaunchViaSteam(out string why) { Calls.Add("launch"); why = Launches ? null : "no steam"; return Launches; }
            public bool InstallElevated(string gameDir, out string why) { Calls.Add("elevated"); why = ElevatedOk ? null : "declined"; return ElevatedOk; }
        }

        [Test("40-6,요청-8")]
        public static void ReinstallClosesInstallsAndRelaunches()
        {
            Func<FakeGameControl, InstallOutcome, bool, ReinstallReport> run = (g, outcome, yes) =>
            {
                var installs = new List<string>();
                var flow = new ReinstallFlow { Game = g, ConfirmRestart = () => { g.Calls.Add("ask"); return yes; }, Install = d => { installs.Add(d); g.Calls.Add("install"); if (g.Running) throw new InvalidOperationException("installed while the game ran"); return new InstallResult { Outcome = g.Calls.Contains("elevated") && !g.RecheckFails ? InstallOutcome.NothingToDo : outcome }; } };
                return flow.Run("G:\\Game");
            };
            var game = new FakeGameControl();
            var r = run(game, InstallOutcome.Done, true);
            A.Eq(ReinstallOutcome.Done, r.Outcome, "game not running: just install");
            A.Eq("install", string.Join(",", game.Calls), "no question, no close, no launch");

            game = new FakeGameControl { Running = true };
            r = run(game, InstallOutcome.Done, false);
            A.Eq(ReinstallOutcome.Cancelled, r.Outcome, "declined");
            A.Eq("ask", string.Join(",", game.Calls), "asked once, nothing else");

            game = new FakeGameControl { Running = true, Closes = false };
            r = run(game, InstallOutcome.Done, true);
            A.Eq(ReinstallOutcome.GameDidNotClose, r.Outcome, "the game would not close");
            A.Eq("ask,close", string.Join(",", game.Calls), "stops before installing");
            A.Eq("timeout:20", r.Why, "reason kept");

            game = new FakeGameControl { Running = true };
            r = run(game, InstallOutcome.Done, true);
            A.Eq(ReinstallOutcome.Relaunched, r.Outcome, "close, install, start again");
            A.Eq("ask,close,install,launch", string.Join(",", game.Calls), "in this order, asked only once");

            game = new FakeGameControl { Running = true };
            r = run(game, InstallOutcome.NeedsElevation, true);
            A.Eq("ask,close,install,elevated,install,launch", string.Join(",", game.Calls), "only the install step again with administrator rights, then checked again");
            A.Eq(ReinstallOutcome.Relaunched, r.Outcome, "then relaunched");

            game = new FakeGameControl { Running = true, ElevatedOk = false };
            r = run(game, InstallOutcome.NeedsElevation, true);
            A.Eq(ReinstallOutcome.Failed, r.Outcome, "elevation refused");
            A.True(r.Elevated && r.Why == "declined", "says why");
            A.False(game.Calls.Contains("launch"), "no relaunch after a failed install");

            game = new FakeGameControl { Running = true, RecheckFails = true };
            r = run(game, InstallOutcome.NeedsElevation, true);
            A.Eq(ReinstallOutcome.Failed, r.Outcome, "the helper said yes but the files are not there: not reported as done");
            A.True(r.Why.StartsWith("not-verified") && r.GameClosed, "says so, and that the game was closed");
            A.False(game.Calls.Contains("launch"), "no relaunch");

            game = new FakeGameControl { Running = true };
            r = run(game, InstallOutcome.Blocked, true);
            A.Eq(ReinstallOutcome.Failed, r.Outcome, "blocked install reported, not relaunched");
            A.False(game.Calls.Contains("launch"), "no relaunch");

            game = new FakeGameControl { Running = true, Launches = false };
            r = run(game, InstallOutcome.Done, true);
            A.Eq(ReinstallOutcome.RelaunchFailed, r.Outcome, "installed but could not start the game");

            using (var rig = new UiRig("reinstall-text", 0))
            {
                var L = (Func<string, string>)rig.Host.Prefs.L;
                var link = Shell.Link;
                A.Eq(L("install.relaunched"), link.ReinstallText(new ReinstallReport { Outcome = ReinstallOutcome.Relaunched, Install = new InstallResult { Outcome = InstallOutcome.Done } }), "relaunch");
                A.True(L("install.relaunched").Contains("Steam") && !L("install.relaunched").Contains("켰어요"), "says Steam was asked, not that the game started");
                A.Eq(L("install.relaunchedNothing"), link.ReinstallText(new ReinstallReport { Outcome = ReinstallOutcome.Relaunched, Install = new InstallResult { Outcome = InstallOutcome.NothingToDo } }), "nothing installed is said");
                var t = link.ReinstallText(new ReinstallReport { Outcome = ReinstallOutcome.Failed, GameClosed = true, Elevated = true, Why = "not-verified:NeedsElevation", Install = new InstallResult { Outcome = InstallOutcome.NeedsElevation } });
                A.True(t.Contains(L("install.why.notVerified")) && t.EndsWith(L("install.gameLeftClosed")), "unverified install, game left closed: " + t);
                A.True(rig.Host.Log.Any(l => l.Contains("reinstall: Failed")), "failures are logged");
                t = link.ReinstallText(new ReinstallReport { Outcome = ReinstallOutcome.Failed, Why = "IOException: x" });
                A.True(t.StartsWith(L("install.failedWhy").Substring(0, 5)), "a failure without install result is not called an elevation failure: " + t);
            }
        }
    }
}
