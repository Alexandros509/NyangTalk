using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Tests
{
    /// <summary>90-SPEC 1.6 (2026-11-20 추가): scroll bars follow the theme; drawn bars keep wheel, drag, click and Page Up/Down.</summary>
    public static class ScrollBarTests
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        private const int WM_MOUSEWHEEL = 0x020A, WM_LBUTTONDOWN = 0x0201, WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_KEYDOWN = 0x0100;

        private static IntPtr XY(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
        private static void Wheel(Control c, int delta)
        {
            var p = c.PointToScreen(new Point(c.Width / 2, c.Height / 2));
            SendMessage(c.Handle, WM_MOUSEWHEEL, (IntPtr)(delta << 16), XY(p.X, p.Y));
        }
        private static void Key(Control c, Keys k) { SendMessage(c.Handle, WM_KEYDOWN, (IntPtr)(int)k, IntPtr.Zero); }

        /// <summary>System greys of scroll bars (classic and Windows 10/11 visual styles).</summary>
        private static readonly Color[] SystemGreys =
        {
            SystemColors.ScrollBar, SystemColors.Control, Color.FromArgb(0xF0, 0xF0, 0xF0), Color.FromArgb(0xCD, 0xCD, 0xCD),
            Color.FromArgb(0xC2, 0xC2, 0xC2), Color.FromArgb(0xA6, 0xA6, 0xA6), Color.FromArgb(0xDA, 0xDA, 0xDA), Color.FromArgb(0xE5, 0xE5, 0xE5)
        };

        /// <summary>Filled areas (3×3 and larger) of a system grey in a picture.</summary>
        public static string GreyIn(Bitmap b)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int w = b.Width, h = b.Height;
                var px = new int[w * h];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                foreach (var grey in SystemGreys)
                {
                    int target = grey.ToArgb() | unchecked((int)0xFF000000), n = 0;
                    for (int y = 1; y < h - 1; y++)
                        for (int x = 1; x < w - 1; x++)
                        {
                            bool all = true;
                            for (int d = 0; d < 9 && all; d++) if ((px[(y + d / 3 - 1) * w + x + d % 3 - 1] | unchecked((int)0xFF000000)) != target) all = false;
                            if (all) n++;
                        }
                    if (n > 0) return grey + " x" + n;
                }
                return null;
            }
            finally { b.UnlockBits(data); }
        }

        private static Bitmap Shot(Control c)
        {
            var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
            c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
            return bmp;
        }

        private static void Hide(Form f) { f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-32000, -32000); f.Opacity = 0; }

        [Test("90-1.6.스크롤")]
        public static void ScrollBarsFollowTheTheme()
        {
            // the checker itself finds a system scroll bar
            using (var probe = new Form { ShowInTaskbar = false })
            {
                Hide(probe);
                var sys = new VScrollBar { Dock = DockStyle.Fill };
                probe.Controls.Add(sys);
                probe.Show();
                using (var b = Shot(sys)) A.True(GreyIn(b) != null, "detector sees the system scroll bar");
            }
            using (var rig = new UiRig("scrollbars", 120))
            {
                var home = rig.Game.LobbyId; var homeName = rig.Game.LobbyName;
                for (int i = 0; i < 25; i++) { rig.Game.LobbyId = "1097752400009" + i.ToString("00000"); rig.Game.LobbyName = "방 " + i; rig.Game.Say(1, "안녕 " + i, rig.Clock.Now); rig.Step(); }
                rig.Game.LobbyId = home; rig.Game.LobbyName = homeName;   // back to the long conversation
                rig.Step(); rig.Step();
                rig.Resize(1100, 640);
                rig.Step();
                var form = rig.Form;
                A.Eq(NyangTalk.Domain.Preferences.LayoutKind.Sidebar, form.CurrentLayout, "sidebar layout shows the saved rooms");
                A.True(form.ArchiveList.Bar.Visible, "saved rooms list needs a scroll bar (" + form.ArchiveList.Items.Count + " rooms)");
                A.True(form.Chat.Bar.Visible && form.Chat.Bar.Enabled, "chat needs a scroll bar");
                form.OpenParticipants();
                var peopleList = form.People.List;
                var search = Dialogs.GlobalSearch(form, rig.Host, h => { });
                Hide(search); search.ClientSize = new Size(420, 160);
                var box = search.Controls.OfType<TextBox>().Single();
                box.Text = "메시지";
                var until = DateTime.Now.AddSeconds(3);
                var searchList = search.Controls.OfType<ThemedList>().Single();
                while (searchList.Items.Count == 0 && DateTime.Now < until) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                A.True(searchList.Bar.Visible, "search results scroll (" + searchList.Items.Count + ")");
                int shots = 0;
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { true, false })
                    {
                        rig.Host.Prefs.Settings.Set("screen.theme", theme);
                        rig.Host.Prefs.Settings.Set("screen.dark", dark);
                        rig.Step();
                        var pal = Dialogs.Themed(new Form(), rig.Host.Prefs);
                        searchList.Pal = pal;   // the open search window keeps its colours; new ones get the current theme
                        if (theme == "sky" && dark) rig.Save("scroll-sky-dark");   // open dialogs keep their colours; new ones get the current theme
                        foreach (var c in new Control[] { form.Chat.Bar, form.ArchiveList, form.ArchiveList.Bar, peopleList, searchList.Bar, form.Main })
                        {
                            using (var b = Shot(c))
                            {
                                var g = GreyIn(b);
                                A.True(g == null, theme + (dark ? "/dark " : "/light ") + c.GetType().Name + " has system grey: " + g);
                                if (theme == "default") b.Save(Path.Combine(Ctx.Scratch, "scroll-" + (dark ? "dark-" : "light-") + c.GetType().Name + shots + ".png"), ImageFormat.Png);
                            }
                            shots++;
                        }
                    }
                form.CloseParticipants(); search.Close();
                Ctx.Measurements.Add("scroll bar pictures: " + shots + " (chat, saved rooms, participants, search; 6 themes x light/dark)");
            }
        }

        [Test("90-1.6.스크롤")]
        public static void DrawnScrollBarsKeepWheelDragClickAndPageKeys()
        {
            using (var rig = new UiRig("scroll-ops", 200))
            {
                var chat = rig.Form.Chat;
                var bar = chat.Bar;
                rig.Form.Chat.ScrollToBottom();
                int bottom = chat.ScrollOffset;
                A.True(bottom > 0, "long chat");
                Wheel(chat, 120);
                A.True(chat.ScrollOffset < bottom, "wheel up scrolls (" + chat.ScrollOffset + " < " + bottom + ")");
                A.Eq(chat.ScrollOffset, bar.Value, "bar follows the wheel");
                int before = chat.ScrollOffset;
                Key(chat, Keys.PageUp);
                A.True(chat.ScrollOffset <= before - chat.ViewportHeight / 2, "Page Up scrolls a page");
                before = chat.ScrollOffset;
                Key(chat, Keys.PageDown);
                A.True(chat.ScrollOffset >= before + chat.ViewportHeight / 2, "Page Down scrolls a page");
                // drag the thumb to the top
                var t = bar.Thumb();
                SendMessage(bar.Handle, WM_LBUTTONDOWN, (IntPtr)1, XY(t.X + 2, t.Y + t.Height / 2));
                SendMessage(bar.Handle, WM_MOUSEMOVE, (IntPtr)1, XY(t.X + 2, t.Y + t.Height / 2 - bar.Height));
                SendMessage(bar.Handle, WM_LBUTTONUP, IntPtr.Zero, XY(t.X + 2, 0));
                A.Eq(0, chat.ScrollOffset, "dragging the thumb to the top scrolls to the start");
                // click the track below the thumb: one page
                t = bar.Thumb();
                SendMessage(bar.Handle, WM_LBUTTONDOWN, (IntPtr)1, XY(t.X + 2, Math.Min(bar.Height - 2, t.Bottom + 10)));
                SendMessage(bar.Handle, WM_LBUTTONUP, IntPtr.Zero, XY(t.X + 2, t.Bottom + 10));
                A.True(chat.ScrollOffset >= chat.ViewportHeight - 2, "a click on the track moves one page (" + chat.ScrollOffset + ")");
                Wheel(bar, -120);
                A.True(bar.Value > chat.ViewportHeight - 2, "wheel over the bar scrolls too");

                // the saved-rooms list
                for (int i = 0; i < 25; i++) { rig.Game.LobbyId = "1097752400008" + i.ToString("00000"); rig.Game.LobbyName = "방 " + i; rig.Game.Say(1, "hi", rig.Clock.Now); rig.Step(); }
                rig.Resize(1100, 600);
                rig.Step();
                var list = rig.Form.ArchiveList;
                A.True(list.Bar.Visible, "saved rooms scroll");
                Wheel(list, -120);
                A.True(list.TopPixel > 0, "wheel scrolls the list");
                Key(list, Keys.Home);
                A.Eq(0, list.SelectedIndex, "Home");
                Key(list, Keys.PageDown);
                A.True(list.SelectedIndex > 1, "Page Down moves the selection a page");
                Key(list, Keys.End);
                A.Eq(list.Items.Count - 1, list.SelectedIndex, "End");
                A.True(list.TopPixel + list.ClientSize.Height >= list.Items.Count * list.ItemHeight - 1, "the last room is scrolled into view");
            }
        }

        [Test("90-1.6,30-5")]
        public static void SearchWindowsFollowTheTheme()
        {
            using (var rig = new UiRig("search-theme", 40))
            {
                var f = rig.Form;
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { true, false })
                    {
                        rig.Host.Prefs.Settings.Set("screen.theme", theme);
                        rig.Host.Prefs.Settings.Set("screen.dark", dark);
                        var th = Theme.Get(theme, dark);
                        f.OpenSearch();
                        f.SearchBox.Text = "메시지";
                        rig.Step();
                        A.Eq(RenderStyle.C(th.Soft).ToArgb(), f.SearchBox.BackColor.ToArgb(), theme + "/" + dark + " room search box blends with the reference strip");
                        A.Eq(RenderStyle.C(th.OtherText).ToArgb(), f.SearchBox.ForeColor.ToArgb(), "search text colour");
                        using (var b = Shot(f.SearchBar)) A.True(GreyIn(b) == null, theme + "/" + dark + " room search bar has system grey: " + GreyIn(b));
                        f.CloseSearch();
                        var g = Dialogs.GlobalSearch(f, rig.Host, h => { });
                        Hide(g);
                        var box = g.Controls.OfType<TextBox>().Single();
                        A.Eq(RenderStyle.C(th.Panel).ToArgb(), g.BackColor.ToArgb(), "search window background");
                        A.Eq(RenderStyle.C(th.OtherBubble).ToArgb(), box.BackColor.ToArgb(), "search window box");
                        using (var b = Shot(g.Controls.OfType<ThemedList>().Single())) A.True(GreyIn(b) == null, "result list");
                        if (theme == "sky" && dark) using (var b = Shot(g)) b.Save(Path.Combine(Ctx.Scratch, "global-search-sky-dark.png"), ImageFormat.Png);
                        g.Close();
                    }
            }
        }

        [Test("30-5")]
        public static void NoFocusFrameAfterAClick()
        {
            using (var rig = new UiRig("focus-frame", 5))
            {
                var b = rig.Form.SettingsButton;
                var other = rig.Form.SearchButton;
                rig.Form.Activate();
                SendMessage(b.Handle, WM_LBUTTONDOWN, (IntPtr)1, XY(5, 5));
                A.True(b.Focused, "the click focused the button");
                A.False(b.FocusRingVisible, "no frame after a mouse click");
                other.Focus();   // as with Tab
                A.True(other.Focused && other.FocusRingVisible, "keyboard focus still shows a ring");
                A.False(b.FocusRingVisible, "and the clicked one has none");
            }
        }

        [Test("60-1,70-2")]
        public static void InitialsAreCentredInTheirCircle()
        {
            foreach (var family in new[] { "Pretendard", "Noto Sans KR", "SUIT", "Malgun Gothic" })
                foreach (var size in new[] { 32, 24, 44 })
                    foreach (var letter in new[] { "M", "보", "ね", "토", "A", "콩" })
                        using (var bmp = new Bitmap(size, size))
                        using (var g = Graphics.FromImage(bmp))
                        using (var f = NyangTalk.App.Platform.Fonts.Create(family, size * 0.3f, FontStyle.Bold))
                        {
                            g.Clear(Color.Black);
                            ChatPainter.DrawCentered(g, letter, f, new RectangleF(0, 0, size, size), Color.White);
                            int minX = size, minY = size, maxX = -1, maxY = -1;
                            for (int y = 0; y < size; y++)
                                for (int x = 0; x < size; x++)
                                    if (bmp.GetPixel(x, y).R > 100) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                            A.True(maxX >= 0, family + " " + letter + " drawn");
                            double cx = (minX + maxX + 1) / 2.0, cy = (minY + maxY + 1) / 2.0;
                            A.True(Math.Abs(cx - size / 2.0) <= 1.0 && Math.Abs(cy - size / 2.0) <= 1.0, family + " " + size + "px '" + letter + "' centre off by " + (cx - size / 2.0) + ", " + (cy - size / 2.0));
                        }
        }

        [Test("90-1.4")]
        public static void PeoplePageShowsMyNicknameAndWholePreview()
        {
            using (var rig = new UiRig("people-page", 5))
            using (var f = SettingsWindowTests.Open(rig))
            {
                var me = rig.Host.Chat.PeopleList().FirstOrDefault(p => p.IsMe);
                A.True(me != null, "the fake game says who I am");
                rig.Host.Prefs.SetAlias(me.SteamId, "고양이 대장");
                f.ShowCategory("people");
                var labels = f.Area.Content.Controls.OfType<Label>().Select(l => l.Text).ToList();
                A.True(labels.Contains(rig.Host.Prefs.L("group.myAlias")), "내 별명 group at the top");
                A.True(labels.Any(t => t.Contains("고양이 대장") && t.Contains(me.Name)), "current nickname shown: " + string.Join(" | ", labels));
                var button = f.Area.Content.Controls.OfType<Button>().First(b => b.Text == rig.Host.Prefs.L("people.myAlias"));
                A.True(button.Enabled, "can set my nickname");
                A.True(labels.IndexOf(rig.Host.Prefs.L("group.myAlias")) < labels.IndexOf(rig.Host.Prefs.L("group.names")), "before the other groups");
                foreach (var cat in new[] { "screen", "chat", "people", "char" })
                {
                    f.ShowCategory(cat);
                    foreach (var v in f.Area.Content.Controls.OfType<NyangTalk.App.Controls.ChatView>())
                        A.True(v.Height >= v.TotalHeight, cat + " preview shows every sample line (" + v.Height + " < " + v.TotalHeight + ")");
                }
                f.ShowCategory("screen");
                rig.Host.Prefs.Settings.Set("screen.preset", "basic");
                A.Eq(rig.Host.Prefs.L("why.screen.tailBubble"), f.Page.Rows["screen.tailBubble"].Note.Text, "a greyed item says why");
                rig.Host.Prefs.Settings.Set("screen.preset", "mobile"); rig.Host.Prefs.Settings.Set("screen.chatShape", "bubble");
                A.Eq("", f.Page.Rows["screen.tailBubble"].Note.Text, "no reason once it is usable");
            }
        }

        [Test("30-5,90-1.4")]
        public static void TooltipStepsAsideAfterAClickAndNoSeam()
        {
            using (var rig = new UiRig("tip-click", 5))
            using (var f = SettingsWindowTests.Open(rig))
            {
                f.ShowCategory("screen");
                var choice = f.Editor("screen.theme");
                var text = rig.Host.Prefs.L("tip.screen.theme");
                A.Eq(text, f.Tips.Active(choice), "tooltip before the click");
                SendMessage(choice.Handle, WM_LBUTTONDOWN, (IntPtr)1, XY(5, 5));
                SendMessage(choice.Handle, WM_LBUTTONUP, IntPtr.Zero, XY(5, 5));
                A.Eq("", f.Tips.Active(choice) ?? "", "no tooltip over the opened list");
                SendMessage(choice.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);   // WM_MOUSELEAVE
                A.Eq(text, f.Tips.Active(choice), "back after the pointer leaves");
                A.Eq(text, f.Tips.Get(choice), "the text itself is kept");
            }
            using (var rig = new UiRig("seam", 5))
            {
                rig.Resize(960, 1000);   // right half of a 1920x1080 screen
                rig.Step();
                foreach (var dark in new[] { false, true })
                {
                    rig.Host.Prefs.Settings.Set("screen.dark", dark);
                    var f = rig.Form;
                    A.True(f.SidebarSplitter.Visible, "sidebar layout at 960 wide");
                    A.Eq(f.Sidebar.BackColor.ToArgb(), f.SidebarSplitter.BackColor.ToArgb(), "the width handle has the sidebar colour (dark " + dark + ")");
                    using (var b = rig.Snapshot())
                    {
                        var p = f.PointToScreen(new Point(f.SidebarSplitter.Left + 1, f.SidebarSplitter.Top + 5));
                        var c = b.GetPixel(p.X - f.Left, p.Y - f.Top);
                        A.Eq(f.Header.BackColor.ToArgb(), c.ToArgb(), "no seam between the sidebar and the top bar");
                    }
                }
            }
        }

        [Test("30-4.2,60-1")]
        public static void ParticipantPillReadsAsAButton()
        {
            using (var rig = new UiRig("pill", 5))
            {
                var faces = rig.Form.Faces;
                A.Eq(AccessibleRole.PushButton, faces.AccessibleRole, "announced as a button");
                foreach (var theme in Theme.Ids)
                    foreach (var dark in new[] { false, true })
                    {
                        rig.Host.Prefs.Settings.Set("screen.theme", theme); rig.Host.Prefs.Settings.Set("screen.dark", dark);
                        var th = Theme.Get(theme, dark);
                        foreach (var hover in new[] { false, true })
                        {
                            SendMessage(faces.Handle, hover ? 0x0200 : 0x02A3, IntPtr.Zero, XY(5, 5));   // move / leave
                            var back = faces.PillBack;
                            var c = NyangTalk.Domain.People.ColorMath.Contrast(th.OtherText, new NyangTalk.Domain.People.Rgb(back.R, back.G, back.B));
                            A.True(c >= 4.5, theme + "/" + dark + (hover ? " hover" : "") + " count text contrast " + c.ToString("0.00"));
                        }
                    }
                int opened = 0;
                faces.Click += (s, e) => opened++;
                Key(faces, Keys.Enter);
                A.Eq(1, opened, "Enter opens the participant list");
                rig.Save("participant-pill");
            }
        }
    }
}
