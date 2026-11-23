using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Platform;
using NyangTalk.Domain.Lobby;

namespace NyangTalk.Tests
{
    public static class ParticipantsLayoutTests
    {
        public static void Check(MainForm form, string context)
        {
            var panel = form.People;
            A.True(panel.Visible, context + " panel is open");
            A.True(panel.Width == Math.Min(DisplayScale.Pixels(ParticipantsPanel.PanelWidth, form.DeviceDpiSafe), form.Main.ClientSize.Width), context + " scaled width");
            A.False(panel.Title.Bounds.IntersectsWith(panel.CloseButton.Bounds), context + " title and close button do not overlap");
            foreach (var label in new[] { panel.Title, panel.Note, panel.Hint }.Where(c => c.Visible))
            {
                int required = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                A.True(label.Height >= required && label.Height >= label.Font.Height, context + " complete wrapped label: " + label.Text);
            }
            foreach (Control c in new Control[] { panel.Title, panel.CloseButton, panel.Note, panel.List, panel.Hint }.Where(c => c.Visible))
                A.True(c.Left >= 0 && c.Right <= panel.Body.Content.Width && c.Top >= 0 && c.Bottom <= panel.Body.Content.Height, context + " content stays inside: " + c.GetType().Name);
            A.True(panel.List.Top >= Math.Max(panel.Title.Bottom, panel.Note.Visible ? panel.Note.Bottom : 0), context + " list starts after status");
            A.True(panel.List.Bottom <= panel.Hint.Top, context + " list ends before the complete hint");
            if (form.ParticipantsOverlay)
            {
                int bandsBottom = Math.Max(form.Header.Bottom, Math.Max(form.NoticeBand.Visible ? form.NoticeBand.Bottom : 0, form.SearchBar.Visible ? form.SearchBar.Bottom : 0));
                A.True(panel.Top >= bandsBottom, context + " overlay stays below header bands");
            }
        }

        [Test("participants,DPI")]
        public static void ParticipantLabelsWrapInEveryLanguageAndFontSize()
        {
            using (var rig = new UiRig("participants-labels", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.layout", "single");
                foreach (var family in new[] { "pretendard", "notosanskr", "suit", "malgun" })
                foreach (int size in new[] { 8, 11, 16 })
                foreach (string lang in new[] { "ko", "en", "ja" })
                {
                    rig.Host.Prefs.Settings.Set("screen.font", family);
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size);
                    rig.Host.Prefs.CompleteFirstRun(lang);
                    rig.Resize(820, 520); rig.Step(); rig.Form.OpenParticipants();
                    var p = rig.Form.People;
                    var members = p.People.ToList();
                    p.Hint.Text = rig.Host.Prefs.L("people.panelHint");
                    foreach (bool stale in new[] { false, true })
                    {
                        string note = stale ? string.Format(rig.Host.Prefs.L("people.lastSeen"), "12:34") : rig.Host.Prefs.L("people.unreadable");
                        p.SetPeople(members, m => m.Name, string.Format(rig.Host.Prefs.L("people.panelTitle"), members.Count), note, stale);
                        Check(rig.Form, family + "/" + size + "/" + lang + "/" + stale);
                    }
                    p.SetPeople(members, m => m.Name, p.Title.Text, null);
                    Check(rig.Form, "readable " + family + "/" + size + "/" + lang);
                    rig.Form.CloseParticipants();
                }
            }
        }

        [Test("participants,DPI")]
        public static void ShortParticipantOverlayKeepsAllTextReachable()
        {
            using (var rig = new UiRig("participants-short", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.layout", "single");
                rig.Host.Prefs.Settings.Set("screen.font", "notosanskr");
                rig.Host.Prefs.Settings.Set("screen.fontSize", 16);
                rig.Resize(300, 360); rig.Form.OpenSearch(); rig.Form.OpenParticipants();
                var p = rig.Form.People;
                p.SetPeople(p.People.ToList(), m => m.Name, p.Title.Text, rig.Host.Prefs.L("people.unreadable"));
                Check(rig.Form, "short overlay");
                A.True(p.Body.MaxOffset > 0, "large font in short panel scrolls rather than clipping text");
                p.Body.EnsureVisible(p.Hint);
                A.True(p.Body.Offset > 0, "complete hint can be reached");
                p.Body.EnsureVisible(p.CloseButton);
                A.Eq(0, p.Body.Offset, "close button can be reached again");
                p.CloseButton.PerformClick();
                A.False(rig.Form.ParticipantsOpen, "close action survives the scroll layout");
            }
        }
    }
}
