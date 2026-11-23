using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Platform;

namespace NyangTalk.Tests
{
    public static class SidebarLayoutTests
    {
        public static void Check(MainForm f, string context)
        {
            A.True(f.SettingsButton.Visible, context + " settings remain in the header");
            A.True(f.SettingsButton.Parent == f.Header, context + " settings belong to the header");
            foreach (Control c in new Control[] { f.Faces, f.SearchButton, f.RoomsButton, f.ReconnectButton, f.MoreButton }.Where(c => c.Visible))
                A.True(c.Right <= f.SettingsButton.Left, context + " settings are rightmost: " + c.GetType().Name);
            HeaderChecks.NoOverlap(f, context);
            A.True(f.Sidebar.Visible, context + " sidebar is expanded");
            if (!f.SidebarOverlay)
                A.True(f.Main.Left >= f.Sidebar.Right, context + " chat makes room for the docked sidebar");
            var blocks = new Control[] { f.AppTitle, f.GlobalSearchButton, f.CurrentHeader, f.CurrentRoomButton,
                f.ArchiveHeader, f.ArchiveList, f.AutomationPanel, f.SidebarBottom }.Where(c => c.Visible).ToArray();
            int bottom = 0;
            foreach (var c in blocks)
            {
                A.True(c.Top >= bottom, context + " vertical sections do not overlap: " + c.GetType().Name);
                A.True(c.Left >= 0 && c.Right <= f.SidebarArea.Content.Width && c.Bottom <= f.SidebarArea.Content.Height,
                    context + " section stays in scroll content: " + c.GetType().Name);
                bottom = c.Bottom;
            }
            foreach (var c in new Control[] { f.AppTitle, f.GlobalSearchButton, f.CurrentHeader, f.CurrentRoomButton,
                f.ArchiveHeader, f.AutomationLabel, f.MyName, f.LinkDot }.Where(c => c.Visible))
                A.True(c.Height >= c.Font.Height, context + " full glyph height fits: " + c.Text);
            A.True(f.MyName.Bottom <= f.LinkDot.Top, context + " footer name and connection status do not overlap");
            A.True(f.LinkDot.Bottom <= f.SidebarBottom.Height, context + " footer fits its container");
            A.Eq(DisplayScale.Pixels(240, f.DeviceDpiSafe), f.Sidebar.Width,
                context + " default sidebar width follows display DPI");
        }

        [Test("sidebar,DPI")]
        public static void SidebarSectionsAndHeaderSurviveFontChanges()
        {
            using (var rig = new UiRig("sidebar-fonts", 4))
            {
                var f = rig.Form;
                f.AutomationStatusText = () => "자동 기능 · 꺼짐";
                rig.Host.Prefs.Settings.Set("screen.layout", "sidebar");
                foreach (var family in new[] { "pretendard", "notosanskr", "suit", "malgun" })
                foreach (var size in new[] { 8, 11, 16 })
                foreach (var buttons in new[] { "pill", "icon" })
                foreach (var height in new[] { 520, 790 })
                {
                    rig.Host.Prefs.Settings.Set("screen.font", family);
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size);
                    rig.Host.Prefs.Settings.Set("screen.headerButtons", buttons);
                    rig.Resize(820, height); rig.Step();
                    Check(f, family + "/" + size + "/" + buttons + "/" + height);
                }
            }
        }

        [Test("sidebar,DPI")]
        public static void DefaultSidebarRemainsExpandedAfterApplyingTheWindowPreset()
        {
            using (var rig = new UiRig("sidebar-default", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.layout", "sidebar");
                rig.Host.Prefs.Settings.Set("screen.windowSize", "default");
                rig.Form.ApplyWindowSize(true); rig.Step();
                Check(rig.Form, "default sidebar");
                A.Eq(0, rig.Form.SidebarArea.MaxOffset, "default sidebar has enough vertical space");
            }
        }

        [Test("sidebar,DPI")]
        public static void ShortOverlayScrollsInsteadOfCrushingItsContents()
        {
            using (var rig = new UiRig("sidebar-overlay-large", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.layout", "single");
                rig.Host.Prefs.Settings.Set("screen.font", "notosanskr");
                rig.Host.Prefs.Settings.Set("screen.fontSize", 16);
                rig.Host.Prefs.Settings.Set("screen.headerButtons", "pill");
                rig.Resize(300, 360); rig.Form.OpenSidebarOverlay(); rig.Step();
                Check(rig.Form, "short overlay");
                A.True(rig.Form.SidebarArea.MaxOffset > 0, "short window scrolls the sidebar");
                rig.Form.SidebarArea.EnsureVisible(rig.Form.LinkDot);
                A.True(rig.Form.SidebarArea.Offset > 0, "footer can be reached");
                rig.Form.SidebarArea.EnsureVisible(rig.Form.CollapseButton);
                A.Eq(0, rig.Form.SidebarArea.Offset, "top can be reached again");
            }
        }
    }
}
