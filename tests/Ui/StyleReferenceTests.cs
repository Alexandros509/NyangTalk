using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using NyangTalk.App.Controls;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;
using NyangTalk.Shared;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.People;

namespace NyangTalk.Tests
{
    public static class StyleReferenceTests
    {
        [Test("50-1,50-7")]
        public static void MultilineDraftRoundTripsWithoutTreatingMarkupAsFormatting()
        {
            using (var rig = new UiRig("multiline", 0))
            {
                var body = "첫 번째 줄\r\n둘째 줄 <b>문자 그대로</b>";
                rig.Form.Input.Text = body;
                A.False(rig.Form.Input.HandleKey(System.Windows.Forms.Keys.Enter, true), "Shift+Enter is left to the multiline editor");
                rig.Form.SendButton.PerformClick(); rig.Step(); rig.Step();
                A.True(rig.Host.Chat.Messages.Any(m => m.Body == body), "line endings and literal tags survive the exchange");
                var part = rig.Form.Chat.Rows.SelectMany(r => r.Parts).Single(p => p.Kind == PartKind.Text && p.Text == body);
                A.True(part.Lines.Count >= 2, "real newline creates separate rendered lines");
                A.True(part.Lines.Any(l => part.Text.Substring(l.Start, l.Length).Contains("<b>")), "plain text is not parsed as markup");
            }
        }

        [Test("90-1,DPI")]
        public static void DemoClearlyIdentifiesThatItDoesNotSendToTheGame()
        {
            var host = new NyangTalk.App.AppHost(Ctx.NewDir("demo-label"), new FakeClock(), true);
            host.Prefs.CompleteFirstRun("ko");
            using (var form = NyangTalk.App.Shell.Create(host))
            {
                form.ApplyStyle(); form.UpdateHeader();
                A.True(form.Text.Contains(host.Prefs.L("demo.title")), "window title labels the demo");
                A.Eq(host.Prefs.L("demo.notice"), form.RoomSub.Text, "status never claims a real game connection");
            }
        }

        [Test("30-7,50-1,90-1")]
        public static void BubbleWidthMatchesRenderedGlyphsWithoutHiddenRightPadding()
        {
            using (var measure = new Measurer())
            using (var bitmap = new Bitmap(500, 140))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var layout = new ChatLayout(measure);
                foreach (string family in new[] { "Pretendard", "Noto Sans KR", "SUIT", "Malgun Gothic" })
                foreach (int size in Enumerable.Range(8, 9))
                foreach (bool mine in new[] { false, true })
                {
                    var style = new RenderStyle { FontFamily = family, FontSize = size, Theme = Theme.Get("peach"), Colors = new PersonColors { Mode = PersonColorMode.None } };
                    var row = layout.Lay(new MessageItem { Message = new ChatMessage { Id = "pad", SenderId = "a", SenderName = "나", IsMine = mine, Body = "테스트", TimestampUtc = Fake.T0 } }, 500, style);
                    var part = row.Parts.Single(p => p.Kind == PartKind.Text);
                    var bubble = row.Parts.Single(p => p.Kind == PartKind.Bubble);
                    var font = measure.Font(style, false, false);
                    int actualWidth = TextRenderer.MeasureText(graphics, part.Text, font, Size.Empty, TextLayout.Flags).Width;
                    A.Eq(actualWidth, part.Rect.Width, family + "/" + size + ": layout width equals target GDI width");
                    graphics.Clear(RenderStyle.C(style.Theme.Window));
                    ChatPainter.Paint(graphics, row, 0, 500, style, measure, null, false, false);
                    int first = part.Rect.Right, last = part.Rect.Left - 1;
                    for (int y = part.Rect.Top; y < part.Rect.Bottom; y++)
                    for (int x = part.Rect.Left; x < part.Rect.Right; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        int toInk = Math.Abs(pixel.R - part.Fore.R) + Math.Abs(pixel.G - part.Fore.G) + Math.Abs(pixel.B - part.Fore.B);
                        int toBack = Math.Abs(pixel.R - bubble.Back.R) + Math.Abs(pixel.G - bubble.Back.G) + Math.Abs(pixel.B - bubble.Back.B);
                        if (toInk < toBack) { first = Math.Min(first, x); last = Math.Max(last, x); }
                    }
                    A.True(last >= first, "visible glyph pixels found");
                    int leftPad = first - bubble.Rect.Left, rightPad = bubble.Rect.Right - last - 1;
                    A.True(Math.Abs(leftPad - rightPad) <= 2, family + "/" + size + ": symmetric visible padding " + leftPad + "/" + rightPad);
                    A.True(rightPad <= 12, "no hidden GDI padding after the final glyph");
                    if (family == "Pretendard" && size == 11 && mine)
                        Ctx.Measurements.Add("내 말풍선 테스트(11pt): 실제 글자 폭 " + actualWidth + "px, 보이는 좌/우 여백 " + leftPad + "/" + rightPad + "px");
                }
            }
        }

        [Test("90-1")]
        public static void PeachOwnBubbleUsesSoftSurfaceAndSendUsesFaceColour()
        {
            foreach (bool dark in new[] { false, true })
            {
                var t = Theme.Get("peach", dark);
                A.Eq(t.Soft, t.MineBubble, "own bubble uses the requested quiet surface");
                A.Eq(t.Text, t.MineText, "own text follows the quiet surface");
                A.Eq("#C9365B", t.Accent.ToHex(), "reference accent on controls remains recorded");
                A.Eq("#DF557C", t.SendFill.ToHex(), "send uses the supplied face background");
                A.True(ColorMath.Contrast(t.SendText, t.SendFill) >= 4.5, "send label remains readable");
                A.True(ColorMath.Contrast(t.MineText, t.MineBubble) >= 4.5, "soft bubble remains readable");
            }
        }

        [Test("30-7,70-2.3,90-1")]
        public static void OwnBubblesKeepTheirBaselineWithTimeAndAvatar()
        {
            using (var measure = new Measurer())
            {
                var layout = new ChatLayout(measure);
                foreach (int size in new[] { 8, 11, 16 })
                foreach (int width in new[] { 300, 510, 820 })
                foreach (bool showCat in new[] { false, true })
                foreach (bool right in new[] { false, true })
                foreach (bool continuation in new[] { false, true })
                foreach (var body in new[] { "테스트", "긴 문장입니다.\n내가 쓴 글과 시간, 아바타가 같은 위치를 유지해야 합니다." })
                {
                    var s = new RenderStyle { FontFamily = "Pretendard", FontSize = size, ShowMyCat = showCat, MyCatRight = right };
                    var row = layout.Lay(new MessageItem { Message = new ChatMessage { Id = "own", SenderId = "self", SenderName = "나", IsMine = true, Body = body, TimestampUtc = Fake.T0 }, IsContinuation = continuation }, width, s);
                    var bubble = row.Parts.Single(p => p.Kind == PartKind.Bubble);
                    var time = row.Parts.SingleOrDefault(p => p.Kind == PartKind.Time);
                    var avatar = row.Parts.SingleOrDefault(p => p.Kind == PartKind.Avatar);
                    A.Eq(s.Pad, bubble.Rect.Top, "no extra head row on the first own message");
                    if (time != null)
                    {
                        A.True(time.Rect.Right < bubble.Rect.Left, "own timestamp sits to the left of the bubble");
                        A.Eq(bubble.Rect.Bottom, time.Rect.Bottom, "time shares the bubble's bottom edge");
                    }
                    if (avatar != null) A.Eq(bubble.Rect.Top, avatar.Rect.Top, "own avatar shares the bubble's top edge");
                    A.Eq(0, RowChecks.Check(row, width).Count, "own text, time and avatar fit without overlap");
                }
            }
        }

        [Test("95-2.6,90-1,30-3")]
        public static void NoticeUsesQuietThemeSurfaceAndTypographyFitsEverySize()
        {
            A.Eq(11, new Settings().GetInt("screen.fontSize"), "readable 11pt default");
            using (var rig = new UiRig("quiet-notice", 0))
            {
                rig.Host.Prefs.Settings.Set("screen.headerButtons", "pill");
                foreach (var theme in Theme.Ids)
                foreach (bool dark in new[] { false, true })
                foreach (int size in new[] { 8, 11, 16 })
                foreach (int width in new[] { 300, 510, 820 })
                {
                    rig.Host.Prefs.Settings.Set("screen.theme", theme); rig.Host.Prefs.Settings.Set("screen.dark", dark);
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size); rig.Resize(width, 720); rig.Form.ApplyStyle();
                    rig.Form.Notices.Set(new NyangTalk.App.NoticeEntry { Key = "offline-preview", Kind = NyangTalk.App.NoticeKind.Connection,
                        Text = "게임 연결 끊김", ActionText = "Steam에서 봉고캣 열기", Action = () => { } });
                    var f = rig.Form;
                    var t = f.CurrentStyle.Theme;
                    A.Eq(RenderStyle.C(t.Soft), f.NoticeBand.BackColor, "notice uses the soft surface");
                    A.Eq(RenderStyle.C(t.Text), f.NoticeLabel.ForeColor, "notice has readable ordinary text");
                    A.True(ColorMath.Contrast(t.Text, t.Soft) >= 4.5, "quiet notice retains readable contrast");
                    HeaderChecks.NoOverlap(f, theme + "/" + size + "/" + width);
                    A.False(f.NoticeLabel.Bounds.IntersectsWith(f.NoticeAction.Bounds), "notice action never covers the sentence");
                    A.True(f.NoticeAction.Bottom <= f.NoticeBand.Height, "notice action fits after resizing");
                    A.True(f.CounterLabel.Height >= f.CounterLabel.Font.Height, "counter line is not vertically clipped");
                    A.True(f.Input.Bottom < f.CounterLabel.Top, "input stays above footer");
                    A.True(f.SettingsButton.Height >= f.SettingsButton.Font.Height + 10, "header grows with its font");
                    if (size == 16) A.True(f.SettingsButton.Font.SizeInPoints > 11, "large text also enlarges pill labels");
                }
            }
        }

        [Test("50-7.필터")]
        public static void RejectedFilterNeverInvokesTransmission()
        {
            int transmissions = 0;
            A.False(FilteredSend.Run(true, () => transmissions++), "filter refusal reported");
            A.Eq(0, transmissions, "no network operation after filter refusal");
            A.True(FilteredSend.Run(false, () => transmissions++), "allowed send invoked");
            A.True(FilteredSend.Run(null, () => transmissions++), "unknown filter leaves validation to the game");
            A.Eq(2, transmissions, "each eligible request transmitted once");
            A.Throws<InvalidOperationException>(() => FilteredSend.Run(false, () => { throw new InvalidOperationException("native send failed"); }), "failure reaches the caller instead of a success receipt");
        }

        [Test("50-7,50-9.7,90-1")]
        public static void SendingStillRoundTripsAfterHeaderAndFontChanges()
        {
            using (var rig = new UiRig("send-after-style", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.headerButtons", "pill");
                foreach (var size in new[] { 8, 10, 16, 12, 10 })
                {
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size);
                    rig.Form.ApplyStyle(); rig.Form.ApplyStyle();
                    var text = "전송 검사 " + size + " " + rig.Clock.Now.Ticks;
                    rig.Form.Input.Text = text; rig.Form.UpdateComposerState();
                    A.True(rig.Form.SendButton.Enabled, "send enabled while connected");
                    rig.Form.SendButton.PerformClick();
                    A.Eq("", rig.Form.Input.Text, "successful submission clears input");
                    rig.Step(); rig.Step();
                    A.True(rig.Host.Chat.Messages.Any(m => m.Body == text), "echo received through file exchange");
                    A.Eq(OutgoingState.Merged, rig.Host.Chat.Outbox.Log.Last().State, "submission paired with game echo");

                    var enterText = "Enter 검사 " + rig.Clock.Now.Ticks;
                    rig.Form.Input.Text = enterText;
                    A.True(rig.Form.Input.HandleKey(Keys.Enter, false), "Enter requests sending");
                    rig.Step(); rig.Step();
                    A.True(rig.Host.Chat.Messages.Any(m => m.Body == enterText), "Enter also round trips");
                }
                rig.Game.FilterResult = "punctuation";
                rig.Form.Input.Text = "거절 검사????"; rig.Form.DoSend(); rig.Step(); rig.Step();
                A.Eq(OutgoingState.NotDelivered, rig.Host.Chat.Outbox.Log.Last().State, "game rejection is visible");
            }
        }

        [Test("90-1,90-1.5,30-3")]
        public static void HeaderPaintSurvivesRepeatedStyleAndFontSizeChanges()
        {
            using (var rig = new UiRig("header-font-lifetime", 4))
            {
                rig.Host.Prefs.Settings.Set("screen.headerButtons", "pill");
                foreach (var family in new[] { "pretendard", "notosanskr", "suit", "malgun", "pretendard" })
                foreach (var size in Enumerable.Range(8, 9).Concat(new[] { 11 }))
                {
                    rig.Host.Prefs.Settings.Set("screen.font", family);
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size);
                    foreach (var width in new[] { 300, 510, 820 })
                    {
                        rig.Resize(width, 720);
                        rig.Form.ApplyStyle(); rig.Form.ApplyStyle();
                        HeaderChecks.NoOverlap(rig.Form, family + "/" + size + "/" + width);
                        int sendTextWidth = TextLayout.MeasureText(rig.Form.SendButton.Text, rig.Form.SendButton.Font,
                            Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                        A.True(rig.Form.SendButton.Width >= sendTextWidth + 16, "large send label fits on one line");
                        foreach (Control c in new Control[] { rig.Form.Header, rig.Form.SettingsButton, rig.Form.MoreButton, rig.Form.SendButton })
                            using (var bitmap = new Bitmap(c.Width, c.Height)) c.DrawToBitmap(bitmap, new Rectangle(Point.Empty, c.Size));
                    }
                }
            }
        }

        [Test("90-1,30-3")]
        public static void ApprovedPaletteAndHeaderStatesMatch()
        {
            object parsed;
            A.True(Json.TryParse(File.ReadAllText(Path.Combine(Ctx.Root, "design-system", "tokens", "approved-palette.json")), out parsed), "reference readable");
            var palettes = Json.Obj(Json.Obj(parsed)["palettes"]);
            using (var rig = new UiRig("approved-style", 12))
            {
                rig.Host.Prefs.Settings.Set("screen.headerButtons", "pill");
                foreach (var id in Theme.Ids)
                foreach (bool dark in new[] { false, true })
                {
                    var t = Theme.Get(id, dark);
                    var actual = new[] { t.Window, t.Panel, t.OtherBubble, t.Soft, t.Border, t.Text, t.MidText, t.SubText, t.FaintText, t.DatePill, t.Notice, t.Accent, t.AccentText };
                    var expected = Json.Obj(palettes[id])[dark ? "dark" : "light"] as IList<object>;
                    A.Eq(13, expected.Count, "13 roles");
                    for (int i = 0; i < actual.Length; i++) A.Eq((string)expected[i], actual[i].ToHex(), id + "/" + dark + " role " + i);
                    rig.Host.Prefs.Settings.Set("screen.theme", id); rig.Host.Prefs.Settings.Set("screen.dark", dark); rig.Step();
                    foreach (int width in new[] { 300, 320, 380, 510, 820, 1100 })
                    {
                        rig.Resize(width, 720); rig.Step();
                        HeaderChecks.NoOverlap(rig.Form, "approved header/" + id + "/" + dark + "/" + width);
                    A.Eq(HeaderPill.HeightFor(rig.Form.SettingsButton.Font), rig.Form.SettingsButton.Height, "height follows the label");
                    }
                    var b = rig.Form.SearchButton;
                    A.Eq(RenderStyle.C(t.Panel), rig.Form.Header.BackColor, "header uses the reference surface");
                    A.Eq(RenderStyle.C(t.Panel), rig.Form.ComposerArea.BackColor, "composer uses the reference surface");
                    A.Eq(RenderStyle.C(t.OtherBubble), rig.Form.Input.BackColor, "editor uses the reference bubble fill");
                    A.Eq(RenderStyle.C(t.Text), rig.Form.Input.ForeColor, "editor uses the reference text");
                    A.Eq(RenderStyle.C(t.Soft), rig.Form.SearchBar.BackColor, "search strip uses the reference soft surface");
                    A.Eq(RenderStyle.C(t.Soft), rig.Form.SearchBox.BackColor, "search editor blends with the soft strip");
                    rig.Form.SendButton.Enabled = true;
                    A.Eq(RenderStyle.C(t.SendFill), rig.Form.SendButton.BackColor, "enabled send uses its theme surface");
                    A.Eq(RenderStyle.C(t.SendText), rig.Form.SendButton.ForeColor, "enabled send uses its theme text");
                    rig.Form.SendButton.Enabled = false;
                    A.Eq(HeaderPill.Composite(RenderStyle.C(t.Panel), RenderStyle.C(t.SendFill), 0.4f), rig.Form.SendButton.BackColor, "disabled send follows the reference opacity");
                    A.Eq(HeaderPill.Composite(RenderStyle.C(t.Panel), RenderStyle.C(t.SendText), 0.4f), rig.Form.SendButton.ForeColor, "disabled send text follows the reference opacity");
                    A.Eq(Color.FromArgb(t.OtherBubble.R, t.OtherBubble.G, t.OtherBubble.B), b.PillFill, "reference bubble colour on header actions");
                    A.Eq(Color.FromArgb(t.Border.R, t.Border.G, t.Border.B), b.PillBorder, "reference border");
                    A.True(rig.Form.Faces.Faces.Count > 0, "participant avatars are preserved");
                    Input(b, "OnMouseLeave", EventArgs.Empty); A.Eq(1f, b.PillVisualOpacity, "normal");
                    Input(b, "OnMouseEnter", EventArgs.Empty); A.Eq(0.8f, b.PillVisualOpacity, "hover");
                    Input(b, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)); A.True(b.PillPressed, "pressed"); A.Eq(0.8f, b.PillVisualOpacity, "press retains the hover appearance");
                    Input(b, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)); Input(b, "OnMouseLeave", EventArgs.Empty);
                    b.Enabled = false; A.Eq(0.4f, b.PillVisualOpacity, "disabled"); b.Enabled = true;
                    Input(b, "OnKeyDown", new KeyEventArgs(Keys.Space)); A.True(b.PillPressed, "Space pressed"); Input(b, "OnKeyUp", new KeyEventArgs(Keys.Space)); A.False(b.PillPressed, "Space released");
                }
                A.Eq(8, HeaderPill.Radius, "8 px radius"); A.Eq(11, HeaderPill.FontPixels, "11 px text");
            }
        }
        public static bool IsApprovedLegacySecondary(Color fore, Color back, Theme theme)
        {
            if (theme.Id != "default" || theme.Dark) return false;
            Func<NyangTalk.Domain.People.Rgb, Color> c = rgb => Color.FromArgb(rgb.R, rgb.G, rgb.B);
            return (fore == c(theme.SubText) && (back == c(theme.Window) || back == c(theme.Panel) || back == c(theme.OtherBubble)))
                || (fore == c(theme.Notice) && back == c(theme.DatePill)) || (fore == c(theme.Warning) && back == c(theme.Window));
        }
        private static void Input(Control control, string method, EventArgs args)
        {
            typeof(IconButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { args });
        }
    }
}
