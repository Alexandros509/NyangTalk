using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Tests
{
    public static class WindowTests
    {
        private static void Key(Control c, Keys k)
        {
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { new KeyEventArgs(k) });
        }

        [Test("50-1.선택")]
        public static void PartialSelectionCopyKeepAndRelease()
        {
            using (var rig = new UiRig("select", 40))
            {
                var body = "가나다 😺 ABC def";
                rig.Game.Say(2, body, rig.Clock.Now);
                rig.Step();
                var chat = rig.Form.Chat;
                chat.ScrollToBottom();
                Func<int> rowIndex = () => chat.Rows.ToList().FindIndex(r => r.Parts.Any(p => p.Kind == PartKind.Text && p.Text == body));
                A.True(rowIndex() >= 0, "row found");
                Func<int, Point> at = idx =>
                {
                    int i = rowIndex();
                    var part = chat.Rows[i].Parts.First(p => p.Kind == PartKind.Text && p.Lines != null);
                    using (var m = new Measurer())
                    {
                        var f = m.Font(chat.Style, false, false);
                        var line = part.Lines.Last(l => l.Start <= idx);
                        return new Point(part.Rect.X + TextLayout.XOf(body, line, idx, f), chat.RowTop(i) - chat.ScrollOffset + part.Rect.Y + line.Top + 3);
                    }
                };
                int afterEmoji = body.IndexOf("😺") + 2;
                chat.PointerDown(at(0), MouseButtons.Left);
                chat.PointerMove(at(afterEmoji));
                chat.PointerUp();
                A.Eq("가나다 😺", chat.SelectedText, "drag selection copies exactly the chosen text (emoji kept whole)");
                System.Threading.Thread.Sleep(SystemInformation.DoubleClickTime + 50);
                chat.PointerDown(new Point(2, 2), MouseButtons.Left); // somewhere else
                A.False(chat.HasTextSelection, "click elsewhere releases");
                System.Threading.Thread.Sleep(SystemInformation.DoubleClickTime + 50);
                chat.PointerDown(at(0), MouseButtons.Left);
                var a = at(afterEmoji - 2); var b = at(afterEmoji);
                chat.PointerMove(new Point((a.X + b.X) / 2 + 1, a.Y));
                var mid = chat.SelectedText;
                A.True(mid == "가나다 " || mid == "가나다 😺", "selection snaps to a whole emoji: [" + mid + "]");
                chat.PointerMove(at(afterEmoji)); chat.PointerUp();
                rig.Game.Say(3, "새 메시지", rig.Clock.Now);
                rig.Step();
                A.Eq("가나다 😺", chat.SelectedText, "kept after a new message");
                chat.ScrollToItemKey(chat.Rows[2].Key);
                chat.ScrollToBottom();
                A.Eq("가나다 😺", chat.SelectedText, "kept after scrolling away and back");
                Key(chat, Keys.Escape);
                A.False(chat.HasTextSelection, "Esc releases");
                A.Eq(body, chat.SelectedText, "nothing chosen: the whole message is copied");
                System.Threading.Thread.Sleep(SystemInformation.DoubleClickTime + 50);
                var p0 = at(body.IndexOf("ABC") + 1);
                chat.PointerDown(p0, MouseButtons.Left); chat.PointerDown(p0, MouseButtons.Left);
                A.Eq("ABC", chat.SelectedText, "double click selects the word");
                chat.PointerDown(p0, MouseButtons.Left);
                A.Eq(body, chat.SelectedText, "triple click selects the whole body");
            }
        }

        [Test("90-1.1")]
        public static void BorderLevelsThemesAndFocus()
        {
            using (var rig = new UiRig("border", 5))
            {
                var f = rig.Form;
                f.Dwm = new FakeDwm { Build = 22631, Fail = true };   // the app-drawn border path (window manager refuses)
                int checkedCount = 0;
                foreach (var theme in Theme.Ids)
                    foreach (var level in new[] { "none", "light", "normal", "strong" })
                        foreach (var active in new[] { true, false })
                        {
                            rig.Host.Prefs.Settings.Set("screen.theme", theme);
                            rig.Host.Prefs.Settings.Set("screen.border", level);
                            f.SetActiveForBorder(active);
                            int want = level == "none" ? 0 : (level == "strong" ? 2 : 1);
                            A.Eq(want, f.BorderThickness, theme + "/" + level + " thickness");
                            A.Eq(want, f.Padding.Left, "padding equals thickness");
                            if (want == 0) continue;
                            using (var bmp = new Bitmap(f.Width, f.Height))
                            {
                                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                                var origin = f.PointToScreen(Point.Empty);
                                int ox = origin.X - f.Left, oy = origin.Y - f.Top;
                                for (int k = 0; k < want; k++)
                                {
                                    var px = bmp.GetPixel(ox + k, oy + f.ClientSize.Height / 2);
                                    A.Eq(f.BorderColor.ToArgb(), px.ToArgb(), theme + "/" + level + "/" + (active ? "active" : "inactive") + " edge pixel " + k);
                                }
                            }
                            checkedCount++;
                        }
                f.SetActiveForBorder(true);
                var activeColor = f.BorderColor;
                f.SetActiveForBorder(false);
                A.True(f.BorderColor != activeColor, "inactive is a step lighter");
                f.MaximizedOverride = true; f.ApplyBorder();
                A.Eq(0, f.BorderThickness, "no border while maximised");
                A.Eq(36, checkedCount, "3 levels x 6 themes x 2 states checked on pixels");
            }
        }

        [Test("90-1.2")]
        public static void OpacityRangeStepPreviewAndHover()
        {
            using (var rig = new UiRig("opacity", 5))
            {
                var s = rig.Host.Prefs.Settings;
                var f = rig.Form;
                A.Eq(100, s.GetInt("screen.opacity"), "default 100%");
                f.ApplyOpacity();
                A.True(Math.Abs(f.Opacity - 1.0) < 0.01, "fully opaque at 100%");
                A.False(s.Set("screen.opacity", "35"), "below 40% refused");
                A.False(s.Set("screen.opacity", "42"), "5% steps only");
                A.True(s.Set("screen.opacity", "40"), "40% accepted");
                A.True(Math.Abs(f.Opacity - 0.40) < 0.01, "applied at once: " + f.Opacity);
                SettingsForm.OpacityPreview(75);
                A.True(Math.Abs(f.Opacity - 0.75) < 0.01, "slider preview shown");
                A.Eq(40, s.GetInt("screen.opacity"), "not saved until released");
                f.ApplyOpacity();
                s.Set("screen.opacityHover", true);
                f.MouseOverWindow = () => true; f.ApplyOpacity();
                A.True(Math.Abs(f.Opacity - 1.0) < 0.01, "mouse over: fully visible");
                f.MouseOverWindow = () => false; f.ApplyOpacity();
                A.True(Math.Abs(f.Opacity - 0.40) < 0.01, "mouse away: back to 40%");
                var again = new NyangTalk.Application.PreferencesService(new NyangTalk.Adapters.Storage.JsonDocumentStore(System.IO.Path.Combine(rig.Dir, "data")));
                again.Load();
                A.Eq(40, again.Settings.GetInt("screen.opacity"), "saved and restored");
            }
        }
    
        [Test("90-1.3")]
        public static void TitleBarFollowsThemeOnWindows11()
        {
            using (var rig = new UiRig("titlebar", 5))
            {
                var f = rig.Form;
                var dwm = new FakeDwm { Build = 22631 };
                f.Dwm = dwm;
                rig.Host.Prefs.Settings.Set("screen.theme", "mono");
                foreach (var theme in Theme.Ids)
                {
                    dwm.Calls.Clear();
                    rig.Host.Prefs.Settings.Set("screen.theme", theme);   // a theme change calls again at once
                    var th = Theme.Get(theme);
                    A.Eq(NyangTalk.App.Platform.TitleBar.ColorRef(th.Panel), dwm.Last(NyangTalk.App.Platform.TitleBar.CaptionColor), theme + " caption = header colour");
                    A.Eq(NyangTalk.App.Platform.TitleBar.ColorRef(th.Text), dwm.Last(NyangTalk.App.Platform.TitleBar.TextColor), theme + " caption text = header text colour");
                    A.Eq(NyangTalk.App.Platform.TitleBar.ColorRef(new NyangTalk.Domain.People.Rgb(f.BorderColor.R, f.BorderColor.G, f.BorderColor.B)), dwm.Last(NyangTalk.App.Platform.TitleBar.BorderColor), theme + " outer line = 1.1 border colour");
                    A.True(f.OsBorder && f.Padding.Left == 0, "the app draws no border of its own");
                }
                rig.Host.Prefs.Settings.Set("screen.border", "none");
                A.Eq(NyangTalk.App.Platform.TitleBar.ColorNone, dwm.Last(NyangTalk.App.Platform.TitleBar.BorderColor), "no border: DWMWA_COLOR_NONE");
                f.Dwm = new FakeDwm { Build = 19045 };   // Windows 10, light themes: nothing is set
                f.ApplyBorder();
                A.Eq(0, ((FakeDwm)f.Dwm).Calls.Count, "Windows 10 with a light theme keeps the default title bar");
                f.Dwm = new FakeDwm { Build = 22631, Throw = true };
                rig.Host.Prefs.Settings.Set("screen.border", "light");
                A.False(f.OsBorder, "failure: no OS border");
                A.Eq(1, f.Padding.Left, "and the app draws the 1.1 border itself");
            }
        }
    }

    public sealed class FakeDwm : NyangTalk.App.Platform.IDwm
    {
        public int Build;
        public bool Fail, Throw;
        public readonly System.Collections.Generic.List<int[]> Calls = new System.Collections.Generic.List<int[]>();
        public int OsBuild { get { return Build; } }
        public int Set(IntPtr hwnd, int attribute, int value)
        {
            if (Throw) throw new InvalidOperationException("simulated");
            Calls.Add(new[] { attribute, value });
            return Fail ? -2147467259 : 0;
        }
        public int Last(int attribute) { for (int i = Calls.Count - 1; i >= 0; i--) if (Calls[i][0] == attribute) return Calls[i][1]; return int.MinValue; }
    }
}
