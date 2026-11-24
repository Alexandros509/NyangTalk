using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Tests
{
    public static class ComposeWorkflowTests
    {
        [Test("send,restore,draft")]
        public static void SubmitClearsImmediatelyAndRestoringNeverSilentlyReplacesANewerDraft()
        {
            using (var rig = new UiRig("restore-workflow", 0))
            {
                string original = "  첫 줄\r\n둘째 줄 ????  ";
                rig.Form.Input.Text = original;
                rig.Form.DoSend();
                A.Eq("", rig.Form.Input.Text, "cleared before any game result or echo");
                var outgoing = rig.Host.Chat.Outbox.Pending.Single();
                A.Eq(OutgoingState.Sending, outgoing.State, "still awaiting the game");
                rig.Form.Input.Text = "다음에 보낼 글";
                rig.Game.FilterResult = "punctuation";
                rig.Step();
                A.Eq(OutgoingState.NotDelivered, outgoing.State, "native filter outcome stays distinct");
                A.Eq("다음에 보낼 글", rig.Form.Input.Text, "rejection never replaces the new draft automatically");
                Func<Row> row = () => rig.Form.Chat.Rows.Single(r => r.Outgoing == outgoing);
                A.True(row().Parts.Any(p => p.Action == "restore"), "restore is available under the rejected message");
                A.False(row().Parts.Any(p => p.Action == "retry"), "filtered content should be edited before sending again");
                rig.Form.OnChatAction("restore", row());
                A.Eq("다음에 보낼 글", rig.Form.Input.Text, "restore asks before replacing a nonempty draft");
                A.True(row().Parts.Any(p => p.Action == "restoreConfirm"), "confirmation appears in the same message");
                rig.Form.OnChatAction("restoreCancel", row());
                A.Eq("다음에 보낼 글", rig.Form.Input.Text, "cancel keeps the draft");
                rig.Form.OnChatAction("restore", row());
                rig.Form.Input.Text += " 계속 작성";
                rig.Form.OnChatAction("restoreConfirm", row());
                A.True(rig.Form.Input.Text.EndsWith("계속 작성"), "confirmation does not overwrite changes made after it appeared");
                rig.Form.OnChatAction("restoreConfirm", row());
                A.Eq(original, rig.Form.Input.Text, "explicit confirmation restores exact whitespace and line breaks");
                A.Eq(1, rig.Host.Chat.Outbox.Log.Count, "restoring never sends anything");
                rig.Form.Input.Clear(); rig.Form.RestoreOutgoing(outgoing.RequestId);
                A.Eq(original, rig.Form.Input.Text, "empty editor restores in one click");
            }
        }

        [Test("send,filter,layout")]
        public static void FilterGuidanceAndRestoreActionsFitAllSupportedLanguagesAndSizes()
        {
            using (var m = new Measurer())
            foreach (var lang in Strings.Languages)
            foreach (int size in new[] { 8, 11, 16 })
            foreach (int width in new[] { 260, 300, 510 })
            foreach (var state in new[] { OutgoingState.Failed, OutgoingState.Rejected, OutgoingState.NotDelivered, OutgoingState.Unconfirmed })
            {
                var style = new RenderStyle { FontSize = size, Lang = lang };
                var o = new OutgoingMessage { RequestId = "test", Text = "???", State = state, Reason = "punctuation", CreatedUtc = Fake.T0 };
                foreach (bool confirm in new[] { false, true })
                {
                    var layout = new ChatLayout(m) { AskingRestore = id => confirm };
                    var row = layout.OutgoingRow(o, width, style);
                    A.Eq(0, RowChecks.Check(row, width).Count, lang + "/" + size + "/" + width + "/" + state + "/" + confirm);
                    string status = row.Parts.First(p => p.Kind == PartKind.Status).Text;
                    A.Eq(state == OutgoingState.NotDelivered, status.Contains(style.L("send.filteredSymbols")), "only filter rejection receives symbols-only guidance");
                    if (state == OutgoingState.Failed) A.Eq(style.L("send.failed"), status, "file/transport failure has ordinary failure text");
                }
            }
        }

        [Test("settings,placement")]
        public static void SettingsPreferAdjacentSpaceAndStayInsideTheOwnersWorkArea()
        {
            var work = new Rectangle(0, 0, 1920, 1040);
            var owner = new Rectangle(100, 100, 765, 900);
            var desired = new Size(1080, 1020); var min = new Size(840, 630);
            var r = Placement.SettingsBounds(owner, desired, min, work, 18);
            A.True(work.Contains(r), "inside the work area at 150% DPI");
            A.False(r.IntersectsWith(owner), "uses the available right side instead of covering the messenger");
            A.Eq(1037, r.Width, "shrinks only enough to fit the right side");
            var rightOwner = new Rectangle(1200, 100, 600, 850);
            r = Placement.SettingsBounds(rightOwner, desired, min, work, 18);
            A.Eq(desired.Width, r.Width, "full-size left side wins over shrinking");
            A.True(r.Right <= rightOwner.Left - 18, "opens on the left near the right edge");
            foreach (var w in new[] { work, new Rectangle(-1920, -200, 1920, 1040), new Rectangle(1920, 0, 1280, 720) })
            foreach (var o in new[] { new Rectangle(w.Left + 400, w.Top + 60, 1000, 800), w, new Rectangle(-32000, -32000, 765, 900) })
            {
                r = Placement.SettingsBounds(o, desired, min, w, 18);
                A.True(w.Contains(r), "also bounded for negative monitor coordinates, small screens and off-screen owners");
                A.True(r.Width > 0 && r.Height > 0, "nonempty usable window");
            }
            var crowded = new Rectangle(650, 100, 1100, 900);
            r = Placement.SettingsBounds(crowded, desired, min, work, 18);
            var centred = new Rectangle(Placement.CenterOn(crowded, desired, work), desired);
            A.True(Rectangle.Intersect(r, crowded).Width * Rectangle.Intersect(r, crowded).Height
                <= Rectangle.Intersect(centred, crowded).Width * Rectangle.Intersect(centred, crowded).Height, "fallback reduces overlap instead of centring over the conversation");
        }

        [Test("shortcuts,settings")]
        public static void ShortcutOptionsRequireSelectionAndOnlyCreateTheChosenLocation()
        {
            using (var rig = new UiRig("shortcut-options", 0))
            using (var settings = Shell.CreateSettings(rig.Host, rig.Form, Shell.Link))
            {
                int startCalls = 0, desktopCalls = 0;
                Shell.Link.CreateShortcut = place =>
                {
                    if (place == NyangTalk.Adapters.GameLink.ShortcutPlace.StartMenu) startCalls++; else desktopCalls++;
                    return "isolated shortcut";
                };
                settings.Show(); settings.ShowCategory("link");
                var start = (ThemedCheck)settings.Controls.Find("shortcut-start", true).Single();
                var desktop = (ThemedCheck)settings.Controls.Find("shortcut-desktop", true).Single();
                var create = (Button)settings.Controls.Find("shortcut-create", true).Single();
                A.False(start.Checked || desktop.Checked, "both destinations are opt-in");
                A.False(create.Enabled, "nothing is created by default");
                start.Checked = true; A.True(create.Enabled, "a choice enables creation"); create.PerformClick();
                A.Eq(1, startCalls, "selected start menu created once"); A.Eq(0, desktopCalls, "unselected desktop is untouched");
                start.Checked = false; desktop.Checked = true; create.PerformClick();
                A.Eq(1, startCalls, "unselected start menu is untouched"); A.Eq(1, desktopCalls, "desktop created only on request");
                Shell.Link.CreateShortcut = place => { throw new System.IO.IOException("denied for test"); };
                create.PerformClick();
                A.True(settings.Controls.Find("shortcut-result", true).Single().Text.Contains("denied for test"), "creation errors stay visible instead of reporting success");
            }
        }
    }
}
