using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NyangTalk.Adapters.Update;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Tests
{
    /// <summary>90-SPEC 1.4–1.6, 30-SPEC 3장, 20-SPEC 2장: settings window, header icons, window sizes, dark mode, update source.</summary>
    public static class SettingsWindowTests
    {
        /// <summary>The wired settings window, off screen and invisible, at an outer size.</summary>
        public static SettingsForm Open(UiRig rig, int w = 720, int h = 680)
        {
            var f = Shell.CreateSettings(rig.Host, rig.Form, Shell.Link);
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-32000, -32000);
            f.Opacity = 0;
            f.Dwm = new FakeDwm { Build = 22631 };
            f.Show();
            f.Size = new Size(w, h);
            System.Windows.Forms.Application.DoEvents();
            return f;
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in All(c)) yield return d;
            }
        }

        /// <summary>The client area only (the title bar is the window manager's, 90-SPEC 1.3).</summary>
        private static Bitmap Client(Form f)
        {
            using (var full = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(full, new Rectangle(Point.Empty, f.Size));
                var origin = f.PointToScreen(Point.Empty);
                var r = new Rectangle(origin.X - f.Left, origin.Y - f.Top, f.ClientSize.Width, f.ClientSize.Height);
                return full.Clone(r, PixelFormat.Format32bppArgb);
            }
        }

        /// <summary>
        /// Pixels of colour <paramref name="c"/> whose whole 3×3 neighbourhood has that colour too: a filled area, as a
        /// control painted in Windows' default grey would leave. Single anti-aliasing pixels at text edges, where two
        /// theme colours happen to blend to the same value, do not count.
        /// </summary>
        private static int CountColor(Bitmap b, Color c)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int w = b.Width, h = b.Height;
                var px = new int[w * h];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                int target = c.ToArgb() | unchecked((int)0xFF000000), n = 0;
                for (int y = 1; y < h - 1; y++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        bool all = true;
                        for (int dy = -1; dy <= 1 && all; dy++)
                            for (int dx = -1; dx <= 1 && all; dx++)
                                if ((px[(y + dy) * w + x + dx] | unchecked((int)0xFF000000)) != target) all = false;
                        if (all) n++;
                    }
                return n;
            }
            finally { b.UnlockBits(data); }
        }

        [Test("90-1.4.1,90-1.6")]
        public static void SettingsWindowFollowsThemeWithoutWindowsGrey()
        {
            using (var rig = new UiRig("settings-theme", 5))
            using (var f = Open(rig))
            {
                var greys = new[] { SystemColors.Control, SystemColors.ControlLight, Color.FromArgb(0xF0, 0xF0, 0xF0) };
                int shots = 0;
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { false, true })
                    {
                        rig.Host.Prefs.Settings.Set("screen.theme", theme);
                        rig.Host.Prefs.Settings.Set("screen.dark", dark);
                        var th = Theme.Get(theme, dark);
                        A.Eq(RenderStyle.C(th.Panel).ToArgb(), f.BackColor.ToArgb(), theme + "/" + dark + " settings surface follows at once");
                        foreach (var cat in SettingsForm.Categories)
                        {
                            f.ShowCategory(cat);
                            System.Windows.Forms.Application.DoEvents();
                            using (var bmp = Client(f))
                            {
                                if (theme == "default" || theme == "mono") bmp.Save(Path.Combine(Ctx.Scratch, "settings-" + theme + (dark ? "-dark-" : "-") + cat + ".png"), ImageFormat.Png);
                                foreach (var g in greys) A.Eq(0, CountColor(bmp, g), theme + "/" + (dark ? "dark" : "light") + "/" + cat + " has no Windows grey " + g);
                                var area = f.Area;
                                var inArea = f.PointToClient(area.PointToScreen(new Point(4, Math.Max(2, area.ClientSize.Height - 3))));
                                var catPt = f.PointToClient(f.CategoryButtons.PointToScreen(new Point(2, f.CategoryButtons.Height - 3)));
                                A.Eq(RenderStyle.C(th.Panel).ToArgb(), bmp.GetPixel(inArea.X, inArea.Y).ToArgb(), cat + " page background = approved settings surface");
                                A.Eq(RenderStyle.C(th.Panel).ToArgb(), bmp.GetPixel(catPt.X, catPt.Y).ToArgb(), cat + " category list = theme panel");
                            }
                            shots++;
                        }
                        var title = ((FakeDwm)f.Dwm).Last(NyangTalk.App.Platform.TitleBar.CaptionColor);
                        A.Eq(NyangTalk.App.Platform.TitleBar.ColorRef(th.Panel), title, "settings title bar follows the theme");
                    }
                A.Eq(6 * 2 * SettingsForm.Categories.Length, shots, "every theme x light/dark x category");
                Ctx.Measurements.Add("settings window pictures: " + shots + " (6 themes x 2 x " + SettingsForm.Categories.Length + " categories), no Windows grey pixel");
            }
        }

        [Test("90-1.4.2")]
        public static void LabelsWrapAndNothingScrollsSideways()
        {
            using (var rig = new UiRig("settings-sizes", 5))
            using (var f = Open(rig))
            {
                foreach (var size in new[] { new Size(720, 680), new Size(560, 420), new Size(1280, 680) })
                {
                    f.Size = size;
                    System.Windows.Forms.Application.DoEvents();
                    foreach (var lang in Strings.Languages)
                    {
                        rig.Host.Prefs.Settings.Set("ui.language", lang);
                        System.Windows.Forms.Application.DoEvents();
                        foreach (var cat in SettingsForm.Categories)
                        {
                            f.ShowCategory(cat);
                            var area = f.Area;
                            string where = size.Width + "x" + size.Height + " " + lang + " " + cat;
                            A.True(area.Content.Width <= area.ClientSize.Width, where + " content not wider than the page");
                            foreach (var c in All(area.Content).Where(c => c.Visible && c.Parent == area.Content))
                            {
                                A.True(c.Right <= area.ContentWidth, where + " " + c.GetType().Name + " '" + c.Text + "' inside the width (" + c.Right + " > " + area.ContentWidth + ")");
                                var l = c as Label;
                                if (l != null && !string.IsNullOrEmpty(l.Text))
                                {
                                    int need = Math.Max(l.GetPreferredSize(new Size(l.Width, 0)).Height, TextRenderer.MeasureText(l.Text, l.Font, new Size(l.Width - l.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + l.Padding.Vertical);
                                    A.True(need <= l.Height + 1, where + " label not cut: '" + l.Text + "' needs " + need + " has " + l.Height);
                                    A.False(l.AutoEllipsis, where + " no … on '" + l.Text + "'");
                                }
                                var cb = c as ThemedCheck;
                                if (cb != null) A.True(cb.Height >= cb.HeightFor(cb.Width), where + " check box text not cut: " + cb.Text);
                            }
                        }
                    }
                    rig.Host.Prefs.Settings.Set("ui.language", "ko");
                }
                A.Eq(SettingsForm.Categories.Length, f.CategoryButtons.Items.Count, "the left category list stays at width 560 (no drop-down instead)");
            }
        }

        [Test("90-1.4.3")]
        public static void NoTwoValueDropDowns()
        {
            using (var rig = new UiRig("settings-two", 5))
            using (var f = Open(rig))
            {
                int checks = 0, radios = 0, lists = 0;
                foreach (var cat in SettingsForm.Categories)
                {
                    f.ShowCategory(cat);
                    foreach (var c in All(f.Area.Content))
                    {
                        A.False(c is ComboBox, cat + ": no system drop-down");
                        var ch = c as ThemedChoice;
                        if (ch != null) { A.True(ch.Items.Length >= 3, cat + ": drop-down '" + ch.AccessibleName + "' has " + ch.Items.Length + " values"); lists++; }
                    }
                    foreach (var key in f.RowKeys)
                    {
                        var d = Settings.Def(key);
                        if (d.Choices != null && d.Choices.Length == 2)
                        {
                            bool onOff = d.Choices.Contains("true");
                            if (onOff) { A.True(f.Editor(key) is ThemedCheck, key + " is a check box"); checks++; }
                            else { A.True(f.Editor(key).Controls.OfType<ThemedCheck>().All(r => r.Radio) && f.Editor(key).Controls.Count == 2, key + " is two radio buttons"); radios++; }
                        }
                    }
                }
                A.True(checks >= 12 && radios >= 7 && lists >= 10, "checks " + checks + ", radios " + radios + ", lists " + lists);
            }
        }

        [Test("90-1.4.4")]
        public static void EverySettingHasATooltipInThreeLanguagesAndARow()
        {
            foreach (var d in Settings.Defs)
                for (int i = 0; i < 3; i++)
                {
                    A.True(Strings.Table.ContainsKey("tip." + d.Key), "tooltip text for " + d.Key);
                    var t = Strings.Table["tip." + d.Key][i];
                    A.True(t.Length >= 8 && t != Strings.Table["set." + d.Key][i], d.Key + " tooltip in " + Strings.Languages[i] + " says more than the label");
                }
            var grouped = SettingsForm.Groups.SelectMany(g => g.Skip(2)).ToList();
            A.Eq(grouped.Count, grouped.Distinct().Count(), "each setting in one group only");
            foreach (var d in Settings.Defs.Where(x => !SettingsForm.NotRows.Contains(x.Key)))
                A.True(grouped.Contains(d.Key), d.Key + " belongs to a group");
            using (var rig = new UiRig("settings-tips", 5))
            using (var f = Open(rig))
            {
                var shown = new HashSet<string>();
                foreach (var cat in SettingsForm.Categories)
                {
                    f.ShowCategory(cat);
                    foreach (var key in f.RowKeys)
                    {
                        shown.Add(key);
                        A.Eq(SettingsForm.CategoryOf(key), cat, key + " shown in its own category");
                        A.Eq(rig.Host.Prefs.L("tip." + key), f.Tips.Get(f.Editor(key)), key + " editor tooltip");
                    }
                }
                foreach (var d in Settings.Defs.Where(x => !SettingsForm.NotRows.Contains(x.Key))) A.True(shown.Contains(d.Key), d.Key + " has a row on screen");
                A.Eq(rig.Host.Prefs.L("tip.ui.language"), f.Tips.Get(f.LanguagePicker), "language picker tooltip");
                A.Eq(HoverTips.DelayMs, f.Tips.InitialDelay, "tooltips after about half a second");
            }
        }

        [Test("90-1.4.5,90-1")]
        public static void RemovedItemsStayRemoved()
        {
            A.True(Settings.Def("screen.gpu") == null, "no graphics acceleration");
            A.True(Settings.Def("update.urlOverride") == null, "no update address field");
            A.False(SettingsForm.Categories.Contains("notify"), "no separate notification category");
            A.Eq("chat", SettingsForm.CategoryOf("notify.mode"), "notifications are a group in the chat category");
            A.False(Strings.Table.ContainsKey("settings.cat.notify"), "no notification category label left");
            using (var rig = new UiRig("settings-removed", 5))
            using (var f = Open(rig))
            {
                foreach (var cat in SettingsForm.Categories)
                {
                    f.ShowCategory(cat);
                    foreach (var b in All(f.Area.Content).OfType<Button>())
                        foreach (var lang in Strings.Languages)
                            A.False(b.Text.Contains("기본값") || b.Text.ToLowerInvariant().Contains("default") || b.Text.Contains("既定"), cat + ": no reset-to-defaults button (" + b.Text + ")");
                    A.False(f.RowKeys.Contains("ui.language"), "language is not a row in a category");
                }
                A.True(f.LanguagePicker.Visible && f.LanguagePicker.Items.Length == 3, "language at the top right");
            }
        }

        [Test("90-1.4")]
        public static void DisabledNotHiddenAndKeyboard()
        {
            using (var rig = new UiRig("settings-keys", 5))
            using (var f = Open(rig))
            {
                var s = rig.Host.Prefs.Settings;
                f.ShowCategory("screen");
                s.Set("screen.preset", "basic");
                A.False(f.IsEditorEnabled("screen.tailBubble"), "tail disabled outside the mobile design");
                A.True(f.Editor("screen.tailBubble").Visible, "and still visible");
                s.Set("screen.preset", "mobile"); s.Set("screen.chatShape", "bubble");
                A.True(f.IsEditorEnabled("screen.tailBubble"), "tail enabled for mobile bubbles");
                A.False(f.IsEditorEnabled("screen.sidebarWidth"), "sidebar width disabled in the single layout");
                s.Set("screen.opacity", 100);
                A.False(f.IsEditorEnabled("screen.opacityHover"), "hover-opaque means nothing at 100%");
                f.ShowCategory("auto");
                A.False(f.IsEditorEnabled("auto.chestTypes") || f.IsEditorEnabled("auto.chestDelay") || f.IsEditorEnabled("auto.chestMaxPerHour"), "chest options disabled while automation is off");
                A.True(f.Page.Rows["auto.chest"].Note.Text.Length > 0, "status line under the switch: " + f.Page.Rows["auto.chest"].Note.Text);
                A.Eq(rig.Host.Prefs.L("auto.status.Off"), f.Page.Rows["auto.gifts"].Note.Text, "gifts off");
                foreach (var l in All(f.Area.Content).OfType<Label>()) A.False(System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"\b(OK|points|limit|permit|busy|Cosmetic)\b"), "no raw codes in the log: " + l.Text);

                // Ctrl+Tab / Ctrl+Shift+Tab, scroll position remembered per category
                f.ShowCategory("screen");
                f.Area.ScrollTo(200);
                int remembered = f.Area.Offset;
                A.True(remembered > 0, "screen page scrolls at 720x680");
                var key = typeof(SettingsForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance);
                var msg = new Message();
                key.Invoke(f, new object[] { msg, Keys.Control | Keys.Tab });
                A.Eq("chat", f.Category, "Ctrl+Tab: next category");
                A.Eq(0, f.Area.Offset, "a new category starts at the top");
                key.Invoke(f, new object[] { msg, Keys.Control | Keys.Shift | Keys.Tab });
                A.Eq("screen", f.Category, "Ctrl+Shift+Tab: previous category");
                A.Eq(remembered, f.Area.Offset, "a visited category keeps its scroll position");
                key.Invoke(f, new object[] { msg, Keys.Escape });
                A.True(f.IsDisposed || !f.Visible, "Esc closes");
            }
        }

        [Test("90-1.4,90-7.2")]
        public static void SaveFailureAndBrokenFileBanner()
        {
            var dir = Ctx.NewDir("settings-broken");
            Directory.CreateDirectory(Path.Combine(dir, "data"));
            File.WriteAllText(Path.Combine(dir, "data", "settings.json"), "{ broken");
            using (var rig = new UiRigAt(dir))
            using (var f = Open(rig.Rig))
            {
                A.True(f.BannerVisible && f.BannerText.Contains("기본값"), "broken file reported at the top: " + f.BannerText);
                var sf = Path.Combine(dir, "data", "settings.json");
                // a running game on this PC makes the app save the found game folder, which recreates the file; the case
                // under test is "the broken file was moved aside and nothing replaced it"
                if (File.Exists(sf)) File.Delete(sf);
                var reload0 = All(f).OfType<Button>().First(b => b.Text == rig.Rig.Host.Prefs.L("settings.reload"));
                reload0.PerformClick();
                A.True(f.BannerVisible && f.BannerText == rig.Rig.Host.Prefs.L("settings.reloadFailed"), "no file to reload: still defaults, said so");
                File.WriteAllText(Path.Combine(dir, "data", "settings.json"), "{\"format\":\"1.0.0\",\"values\":{\"screen.theme\":\"mint\"}}");
                var reload = All(f).OfType<Button>().First(b => b.Text == rig.Rig.Host.Prefs.L("settings.reload"));
                reload.PerformClick();
                A.False(f.BannerVisible, "reloaded");
                A.Eq("mint", rig.Rig.Host.Prefs.Settings.Get("screen.theme"), "values from the repaired file");
                NyangTalk.Adapters.Storage.AtomicFile.FailureInjector = p => new IOException("locked (test)");
                int retries = NyangTalk.Adapters.Storage.AtomicFile.Retries; NyangTalk.Adapters.Storage.AtomicFile.Retries = 1;
                try { rig.Rig.Host.Prefs.Settings.Set("screen.theme", "sky"); }
                finally { NyangTalk.Adapters.Storage.AtomicFile.FailureInjector = null; NyangTalk.Adapters.Storage.AtomicFile.Retries = retries; }
                A.True(f.BannerVisible && f.BannerText == rig.Rig.Host.Prefs.L("settings.saveFailed"), "save failure shown: " + f.BannerText);
                A.Eq("sky", rig.Rig.Host.Prefs.Settings.Get("screen.theme"), "still applied");
            }
        }

        [Test("90-1.5")]
        public static void WindowSizeStepsAndCustom()
        {
            using (var rig = new UiRig("winsize", 40))
            {
                var form = rig.Form;
                var s = rig.Host.Prefs.Settings;
                A.Eq("default", s.Get("screen.windowSize"), "default step");
                int dpi = form.DeviceDpiSafe;
                A.Eq(new Size(300, 360), form.MinimumSize, "single layout minimum 300x360");
                foreach (var step in WindowSizes.Steps)
                {
                    s.Set("screen.windowSize", step);
                    System.Windows.Forms.Application.DoEvents();
                    var px = WindowSizes.Pixels(step, dpi);
                    A.Eq(new Size(px[0], px[1]), form.Size, step + " applied");
                    A.Eq(NyangTalk.Domain.Preferences.LayoutKind.Single, form.CurrentLayout, step + " is a single-window size with layout auto");
                    foreach (var shape in new[] { "bubble", "card", "compact" })
                    {
                        s.Set("screen.chatShape", shape);
                        rig.Step();
                        HeaderChecks.NoOverlap(form, step + "/" + shape);
                    }
                }
                using (var f = Open(rig))
                {
                    f.ShowCategory("screen");
                    form.Size = new Size(450, 600);
                    form.OnUserResized();
                    A.Eq(WindowSizes.Custom, s.Get("screen.windowSize"), "dragged: custom");
                    A.True(f.WindowSizeNote().Contains("450 × 600") && f.WindowSizeNote().Contains(rig.Host.Prefs.L("settings.windowCustom")), "real size and custom shown: " + f.WindowSizeNote());
                    A.Eq(rig.Host.Prefs.L("val.screen.windowSize.custom"), ((ThemedChoice)f.Editor("screen.windowSize")).SelectedText, "the picker says 직접 조절");
                    f.MainWindowDpi = () => 120;
                    A.True(f.WindowSizeNote().Contains("125%"), "display scale shown: " + f.WindowSizeNote());
                    f.MainWindowDpi = () => 144;
                    A.True(f.WindowSizeNote().Contains("150%"), "display scale shown: " + f.WindowSizeNote());
                }
                form.Close();   // saves the dragged size
                var again = new NyangTalk.Application.PreferencesService(new NyangTalk.Adapters.Storage.JsonDocumentStore(Path.Combine(rig.Dir, "data")));
                again.Load();
                A.Eq("450,600", string.Join(",", again.LoadWindowSize("main")), "dragged size remembered");
            }
        }

        [Test("30-3,30-5")]
        public static void HeaderIconsTooltipsAndStatusLine()
        {
            using (var rig = new UiRig("header", 20))
            {
                var f = rig.Form;
                var s = rig.Host.Prefs.Settings;
                s.Set("screen.windowSize", "default");
                rig.Step();
                A.False(f.MoreButton.Visible, "no menu button in the header at the default size");
                foreach (var b in new Control[] { f.RoomsButton, f.SearchButton, f.ReconnectButton, f.SettingsButton })
                {
                    A.True(b.Visible, b.AccessibleName + " visible in a row");
                    A.True(!string.IsNullOrEmpty(f.Tips.Get(b)), "tooltip on " + b.AccessibleName);
                }
                A.Eq("검색 (Ctrl+F)", f.Tips.Get(f.SearchButton), "name and shortcut");
                A.True(f.Tips.InitialDelay >= 400 && f.Tips.InitialDelay <= 700, "delay " + f.Tips.InitialDelay);
                A.True(f.Tips.Get(f.CollapseButton).Contains("Ctrl+B") && f.Tips.Get(f.GlobalSearchButton).Contains("Ctrl+K"), "sidebar buttons carry the same tooltips");
                var menu = f.RoomsMenu();
                A.True(menu.Items.Cast<ToolStripItem>().Any(i => i.Text == rig.Host.Prefs.L("room.current")), "rooms popup lists the current lobby");
                A.True(menu.Items.Cast<ToolStripItem>().Any(i => i.Text == rig.Host.Prefs.L("room.archived")), "and the saved rooms");
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { false, true })
                        foreach (var preset in new[] { "basic", "mobile", "business", "wire" })
                            foreach (var size in new[] { 8, 10, 16 })
                            {
                                s.Set("screen.theme", theme); s.Set("screen.dark", dark); s.Set("screen.preset", preset); s.Set("screen.fontSize", size);
                                var c = ColorMath.Contrast(new Rgb(f.RoomSub.ForeColor.R, f.RoomSub.ForeColor.G, f.RoomSub.ForeColor.B), new Rgb(f.Header.BackColor.R, f.Header.BackColor.G, f.Header.BackColor.B));
                                A.True(c >= 4.5 || StyleReferenceTests.IsApprovedLegacySecondary(f.RoomSub.ForeColor, f.Header.BackColor, Theme.Get(theme, dark)), theme + "/" + dark + "/" + preset + " status line contrast " + c.ToString("0.00"));
                                A.True(f.RoomSub.Font.SizeInPoints >= 9 && f.RoomSub.Font.SizeInPoints <= 10, "status line 9-10 pt: " + f.RoomSub.Font.SizeInPoints);
                                A.True(f.RoomSub.Font.SizeInPoints < f.RoomTitle.Font.SizeInPoints, "smaller than the room name at body " + size);
                                A.True(f.RoomSub.Bottom <= f.Header.ClientSize.Height, "status line inside the header at " + size + " pt");
                            }
                s.Set("screen.fontSize", 10);
                A.Eq(f.RoomSub.Text, f.Tips.Get(f.RoomSub), "the full sentence in the status line tooltip");

                // mini: less used icons go into "…", nothing overlaps
                s.Set("screen.windowSize", "mini");
                rig.Step();
                HeaderChecks.NoOverlap(f, "mini");
                rig.Save("mini-header");
                A.True(f.MoreButton.Visible, "mini needs the 더 보기 button");
                A.True(f.SearchButton.Visible && f.SettingsButton.Visible, "search and settings stay");
                A.True(f.MoreMenu().Items.Count == f.Overflow.Count, "each hidden icon is in the overflow menu");
                A.Eq(f.Tips.Get(f.MoreButton), rig.Host.Prefs.L("tip.header.more"), "overflow tooltip");
            }
        }

        [Test("90-1.6")]
        public static void DarkModeAppliesEverywhereAtOnce()
        {
            using (var rig = new UiRig("dark", 20))
            using (var f = Open(rig))
            {
                var dwm = new FakeDwm { Build = 22631 };
                rig.Form.Dwm = dwm;
                rig.Host.Prefs.Settings.Set("screen.theme", "sky");
                rig.Host.Prefs.Settings.Set("screen.dark", true);
                var th = Theme.Get("sky", true);
                A.Eq(RenderStyle.C(th.Panel).ToArgb(), f.BackColor.ToArgb(), "settings surface dark at once");
                A.True(rig.Form.CurrentStyle.Theme.Dark, "chat style dark");
                A.Eq(RenderStyle.C(th.OtherBubble).ToArgb(), rig.Form.Input.BackColor.ToArgb(), "input box follows (no white box)");
                A.Eq(1, dwm.Last(NyangTalk.App.Platform.TitleBar.DarkMode), "title bar buttons dark");
                A.Eq(NyangTalk.App.Platform.TitleBar.ColorRef(th.Panel), dwm.Last(NyangTalk.App.Platform.TitleBar.CaptionColor), "title bar colour");
                foreach (var row in rig.Form.Chat.Rows)
                    A.True(RowChecks.Contrast(row, rig.Form.CurrentStyle).Count == 0, "dark rows readable: " + string.Join("; ", RowChecks.Contrast(row, rig.Form.CurrentStyle)));
                rig.Save("dark-sky");
                rig.Host.Prefs.Settings.Set("screen.dark", false);
                A.Eq(0, dwm.Last(NyangTalk.App.Platform.TitleBar.DarkMode), "light again");
            }
        }

        [Test("90-1.4,70-1")]
        public static void NotificationPreviewLeavesUnreadAlone()
        {
            using (var rig = new UiRig("notify-preview", 5))
            {
                int before = rig.Host.Chat.Unread.Unread;
                A.Eq(rig.Host.Prefs.L("settings.notifyPreviewBadge"), rig.Form.PreviewNotification("flashBadge"), "badge preview");
                A.Eq(rig.Host.Prefs.L("settings.notifyPreviewOff"), rig.Form.PreviewNotification("off"), "nothing when off");
                A.Eq(before, rig.Host.Chat.Unread.Unread, "real unread count unchanged");
            }
        }

        [Test("90-3")]
        public static void LanguageDefaultsToKorean()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentUICulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
                var p = new NyangTalk.Application.PreferencesService(new NyangTalk.Adapters.Storage.JsonDocumentStore(Ctx.NewDir("lang-default")));
                p.Load();
                A.Eq("ko", p.Lang, "Korean on an English Windows");
            }
            finally { System.Threading.Thread.CurrentThread.CurrentUICulture = old; }
        }

        [Test("20-2")]
        public static void UpdateSourceIsFixedWithTestOverride()
        {
            A.Eq("https://github.com/Alexandros509/NyangTalk/releases/latest/download/update.json", UpdateSource.DefaultInfoUrl, "built-in address");
            A.Eq(UpdateSource.DefaultInfoUrl, new UpdateSource().InfoUrl("stable").ToString(), "stable channel");
            A.True(new UpdateSource().InfoUrl("beta").ToString().EndsWith("/update-beta.json"), "beta file");
            A.True(UpdateSource.FromOverrideText("https://example.test/u.json\r\n").Overridden, "one HTTPS line is used");
            A.False(UpdateSource.FromOverrideText("http://example.test/u.json").Overridden, "HTTP ignored");
            A.False(UpdateSource.FromOverrideText("example.test/u.json").Overridden, "not an address: ignored");
            A.False(UpdateSource.FromOverrideText("https://a.test/1\nhttps://b.test/2").Overridden, "two lines: ignored");
            A.False(UpdateSource.FromOverrideText("").Overridden, "empty: ignored");
            A.Eq(UpdateSource.DefaultInfoUrl, UpdateSource.FromOverrideText("file:///c:/x.json").InfoUrl("stable").ToString(), "invalid: default used");

            using (var rig = new UiRig("update-source", 5))
            {
                File.WriteAllText(Path.Combine(rig.Dir, UpdateSource.OverrideFile), "https://example.test/update.json");
                using (var f = Open(rig))
                {
                    f.ShowCategory("about");
                    var texts = All(f.Area.Content).Select(c => c.Text).ToList();
                    A.True(texts.Contains("업데이트 받는 곳: github.com/Alexandros509/NyangTalk"), "read-only source line");
                    A.False(All(f.Area.Content).Any(c => c is TextBox), "no address field");
                    var warn = All(f.Area.Content).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("시험용 주소 사용 중"));
                    A.True(warn != null && warn.ForeColor.ToArgb() == f.Pal.Warning.ToArgb(), "test address shown in the warning colour");
                    var link = All(f.Area.Content).OfType<LinkLabel>().Single();
                    A.True(f.Tips.Get(link).Length > 0, "source tooltip");
                    var mode = f.Editor("update.mode");
                    A.True(All(f.Area.Content).OfType<Button>().First(b => b.Text == rig.Host.Prefs.L("update.checkNow")).Top < mode.Top && mode.Top < link.Top, "order: check now, then how updates arrive, then the source");
                }
                Shell.Link.UpdateSource = null;
                File.Delete(Path.Combine(rig.Dir, UpdateSource.OverrideFile));
                // no network and no dependence on the key built into the app: a fake web and explicit keys
                var web = new FakeHttp();
                Shell.Link.UpdateHttp = web;
                Shell.Link.UpdateKeys = new List<byte[]>();
                Shell.Link.RunUpdateCheck();
                A.True(Shell.Link.LastUpdateText.StartsWith("확인하지 못했어요") && Shell.Link.LastUpdateText.Contains(rig.Host.Prefs.L("update.noKey")), "no key: could not check, says why: " + Shell.Link.LastUpdateText);
                A.Eq(0, web.Requests.Count, "no request without a key");
                Shell.Link.UpdateKeys = new List<byte[]> { NyangTalk.Adapters.Update.Signatures.PublicOf(System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256)) };
                Shell.Link.RunUpdateCheck();   // a key, but no release yet (404)
                A.True(Shell.Link.LastUpdateText.StartsWith("확인하지 못했어요") && !Shell.Link.LastUpdateText.Contains(rig.Host.Prefs.L("update.noKey")), "before the first release: could not check · time: " + Shell.Link.LastUpdateText);
                A.Eq(UpdateSource.DefaultInfoUrl, web.Requests[0], "asks the built-in address");
                A.False(Shell.Link.LastUpdateText.Contains("최신"), "never claims up to date");
                A.True(Shell.Link.LastUpdateBand == null, "no new-version band");
            }
        }
    }

    public static class HeaderChecks
    {
        /// <summary>Header icons, title, status line and the composer's input and send button do not overlap.</summary>
        public static void NoOverlap(MainForm f, string where)
        {
            var parts = f.Header.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                A.True(parts[i].Left >= 0 && parts[i].Right <= f.Header.ClientSize.Width, where + ": " + parts[i].AccessibleName + " inside the header");
                for (int j = i + 1; j < parts.Count; j++)
                {
                    bool textPair = (parts[i] == f.RoomTitle && parts[j] == f.RoomSub) || (parts[i] == f.RoomSub && parts[j] == f.RoomTitle);
                    if (!textPair) A.False(parts[i].Bounds.IntersectsWith(parts[j].Bounds), where + ": " + (parts[i].AccessibleName ?? parts[i].Name) + " overlaps " + (parts[j].AccessibleName ?? parts[j].Name));
                }
            }
            A.True(f.RoomTitle.Width >= 40, where + ": room name has room");
            A.False(f.Input.Bounds.IntersectsWith(f.SendButton.Bounds), where + ": input and send do not overlap");
            A.True(f.SendButton.Right <= f.ComposerArea.ClientSize.Width, where + ": send inside");
        }
    }

    /// <summary>A rig on a prepared data folder.</summary>
    public sealed class UiRigAt : IDisposable
    {
        public UiRig Rig;
        public UiRigAt(string dir) { Rig = new UiRig("unused", 0, dir); }
        public void Dispose() { Rig.Dispose(); }
    }
}
