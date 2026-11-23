using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.App.Demo;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Tests
{
    /// <summary>The real main window on a fake game, driven without showing it on screen.</summary>
    public sealed class UiRig : IDisposable
    {
        public FakeClock Clock = new FakeClock();
        public AppHost Host;
        public FakeGame Game;
        public MainForm Form;
        public string Dir;

        public UiRig(string name, int messages = 30, string dir = null, bool firstRun = false)
        {
            Dir = dir ?? Ctx.NewDir("ui-" + name);
            Host = new AppHost(Dir, Clock, false);
            if (!firstRun && Host.Prefs.FirstRun) Host.Prefs.CompleteFirstRun("ko");
            Game = new FakeGame(Path.Combine(Dir, "exchange"));
            Form = Shell.Create(Host);
            Shell.Link.RunKey = new FakeRunKey();          // never the real start-up list
            Shell.Link.StartWatcher = exe => { };          // never a real background watcher
            Form.ShowInTaskbar = false;
            Form.StartPosition = FormStartPosition.Manual;
            Form.Location = new Point(-32000, -32000);
            Form.Opacity = 0;
            Form.Show();
            Step();
            for (int i = 0; i < messages; i++) Game.Say(1 + i % 3, i % 5 == 0 ? "조금 긴 메시지입니다. 줄바꿈이 필요할 수도 있는 길이의 문장을 넣어 봅니다 " + i : "메시지 " + i, Clock.Now.AddSeconds(-messages + i));
            Step();
        }

        public void Step(double seconds = 1)
        {
            Clock.Now = Clock.Now.AddSeconds(seconds);
            Game.WriteStatus(Clock.Now);
            Game.WriteParticipants();
            Game.ProcessOutbox(Clock.Now);
            Form.Tick();
            System.Windows.Forms.Application.DoEvents();
        }

        public void Resize(int w, int h)
        {
            Form.MinimumSize = Size.Empty;
            Form.ClientSize = new Size(w, h);
            Form.PerformLayout();
            System.Windows.Forms.Application.DoEvents();
        }

        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(Form.Width, Form.Height);
            Form.DrawToBitmap(bmp, new Rectangle(Point.Empty, Form.Size));
            return bmp;
        }

        public void Save(string name)
        {
            var dir = Path.Combine(Ctx.Root, "build", "screens");
            Directory.CreateDirectory(dir);
            using (var b = Snapshot()) b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
        }

        public void Dispose() { Form.Dispose(); }
    }

    /// <summary>In-memory start-up list for checks (95-SPEC 2.1: never the real registry).</summary>
    public sealed class FakeRunKey : NyangTalk.Adapters.GameLink.IRunKey
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public bool Fail;
        public string Get(string name) { string v; return Values.TryGetValue(name, out v) ? v : null; }
        public bool Set(string name, string value, out string why) { why = Fail ? "denied (test)" : null; if (!Fail) Values[name] = value; return !Fail; }
        public bool Delete(string name, out string why) { why = Fail ? "denied (test)" : null; if (!Fail) Values.Remove(name); return !Fail; }
    }

    public static class RowChecks
    {
        /// <summary>No overlap between avatar, name, time, bubble, buttons; everything inside the row; text inside its bubble.</summary>
        public static List<string> Check(Row r, int width)
        {
            var bad = new List<string>();
            var solid = r.Parts.Where(p => p.Kind == PartKind.Avatar || p.Kind == PartKind.Name || p.Kind == PartKind.Time || p.Kind == PartKind.Button || p.Kind == PartKind.Status).ToList();
            var bubble = r.Parts.FirstOrDefault(p => p.Kind == PartKind.Bubble);
            foreach (var p in r.Parts)
            {
                if (p.Rect.Left < 0 || p.Rect.Right > width || p.Rect.Top < 0 || p.Rect.Bottom > r.Height)
                    bad.Add(r.Kind + " " + p.Kind + " outside row " + p.Rect + " w=" + width + " h=" + r.Height);
            }
            for (int i = 0; i < solid.Count; i++)
                for (int j = i + 1; j < solid.Count; j++)
                    if (solid[i].Rect.IntersectsWith(solid[j].Rect))
                        bad.Add(r.Kind + " " + solid[i].Kind + " overlaps " + solid[j].Kind);
            var avatar = r.Parts.FirstOrDefault(p => p.Kind == PartKind.Avatar);
            if (avatar != null && bubble != null && r.Kind != RowKind.More && avatar.Rect.IntersectsWith(bubble.Rect) && bubble.Rect.Width < width - 20)
                bad.Add(r.Kind + " avatar overlaps bubble");
            if (bubble != null && bubble.Rect.Width < width - 20)
                foreach (var p in r.Parts.Where(p => p.Kind == PartKind.Text || (p.Kind == PartKind.Name && p.Rect.IntersectsWith(bubble.Rect))))
                    if (!bubble.Rect.Contains(p.Rect)) bad.Add(r.Kind + " " + p.Kind + " not inside bubble");
            return bad;
        }

        private static bool InBubble(Part p, Part bubble) { return bubble != null && bubble.Rect.Contains(p.Rect); }

        /// <summary>Text contrast of each drawn text against what is under it (≥ 4.5:1).</summary>
        public static List<string> Contrast(Row r, RenderStyle s)
        {
            var bad = new List<string>();
            var bubble = r.Parts.FirstOrDefault(p => p.Kind == PartKind.Bubble);
            foreach (var p in r.Parts.Where(p => p.Kind == PartKind.Text || p.Kind == PartKind.Name || p.Kind == PartKind.Time || p.Kind == PartKind.Status || p.Kind == PartKind.Center))
            {
                Color under = RenderStyle.C(s.Theme.Window);
                if (bubble != null && bubble.Rect.Contains(p.Rect) && !bubble.Back.IsEmpty) under = bubble.Back;
                if (p.Kind == PartKind.Center && !p.Back.IsEmpty) under = p.Back;
                var c = ColorMath.Contrast(new Rgb(p.Fore.R, p.Fore.G, p.Fore.B), new Rgb(under.R, under.G, under.B));
                if (c < 4.5 && !StyleReferenceTests.IsApprovedLegacySecondary(p.Fore, under, s.Theme)) bad.Add(r.Kind + " " + p.Kind + " contrast " + c.ToString("0.00") + " (" + p.Fore + " on " + under + ")");
            }
            return bad;
        }
    }

    public static class UiTests
    {
        private static List<TimelineItem> SampleItems()
        {
            var ms = new List<ChatMessage>();
            int t = 0;
            ms.Add(Fake.Text("76561190000000002", "안녕하세요 😺", t++));
            ms.Add(Fake.Text("76561190000000002", "이어서 보낸 말", t++));
            ms.Add(Fake.Text("76561190000000003", "A rather long English message that has to wrap over more than one line in narrow windows, with <b>markup</b> kept as text.", t++));
            ms.Add(Fake.Text("me", "내 메시지", t++, true));
            ms.Add(Fake.Text("me", "아주 긴 내 메시지 ".PadRight(160, '가'), t++, true));
            ms.Add(Fake.Emote("76561190000000004", "heart", t++)); ms.Add(Fake.Emote("76561190000000004", "heart", t++));
            ms.Add(Fake.Text("76561190000000003", "Meow Meow", t++)); ms.Add(Fake.Text("76561190000000002", "야옹 야옹", t++));
            ms.Add(Fake.Text("76561190000000005", "x", t++));
            return TimelineBuilder.Build(ms, new TimelineOptions());
        }

        private static RenderStyle Style(string theme, ChatShape shape, DesignPreset preset)
        {
            return new RenderStyle { Theme = Theme.Get(theme), Shape = shape, Preset = preset, Colors = new PersonColors { Mode = PersonColorMode.Auto } };
        }

        /// <summary>30-8.7, 7.2-1, 60-5.4, 70-3.6: every combination laid out at two widths, no overlap/clipping, contrast ≥ 4.5.</summary>
        [Test("30-8.7,30-7.2.1,60-5.3,60-5.4,70-3.6")]
        public static void AllCombinationsLayoutAndContrast()
        {
            var items = SampleItems();
            var outgoing = new OutgoingMessage { RequestId = "r", Text = "실패한 글", State = OutgoingState.Rejected, Reason = "x", CreatedUtc = Fake.T0 };
            int combos = 0;
            var problems = new List<string>();
            using (var m = new Measurer())
            {
                var layout = new ChatLayout(m) { TimeZone = TimeZoneInfo.Utc };
                foreach (var width in new[] { 360 - 30, 1100 - 260 })
                    foreach (var theme in Theme.Ids)
                        foreach (ChatShape shape in Enum.GetValues(typeof(ChatShape)))
                            foreach (DesignPreset preset in Enum.GetValues(typeof(DesignPreset)))
                                foreach (ColorPlacement place in Enum.GetValues(typeof(ColorPlacement)))
                                    foreach (var mine in new[] { "hide", "left", "right" })
                                    {
                                        var s = Style(theme, shape, preset);
                                        s.Colors.Placement = place;
                                        s.ShowMyCat = mine != "hide"; s.MyCatRight = mine == "right";
                                        var tag = width + "/" + theme + "/" + shape + "/" + preset + "/" + place + "/" + mine;
                                        var rows = items.Select(it => layout.Lay(it, width, s)).ToList();
                                        rows.Add(layout.OutgoingRow(outgoing, width, s));
                                        rows.Add(layout.MoreRow(true, true, width, s));
                                        foreach (var r in rows)
                                        {
                                            problems.AddRange(RowChecks.Check(r, width).Select(x => tag + ": " + x));
                                            problems.AddRange(RowChecks.Contrast(r, s).Select(x => tag + ": " + x));
                                        }
                                        combos++;
                                    }
            }
            A.True(problems.Count == 0, problems.Count + " problems, first: " + string.Join(" | ", problems.Take(16)));
            Ctx.Measurements.Add("배치 계산 검사 조합 수: " + combos + " (너비 2 × 테마 6 × 채팅 모양 3 × 프리셋 4 × 색 위치 3 × 내 캐릭터 3)");
        }

        /// <summary>Render PNGs of the real window: 2 layouts × 3 shapes × 4 presets × 6 themes (30-7.2.1, 30-8.7).</summary>
        [Test("30-7.2.1,30-8.7,01-6")]
        public static void RenderAllScreensToPng()
        {
            using (var rig = new UiRig("png", 25))
            {
                int n = 0;
                foreach (var layout in new[] { "single", "sidebar" })
                {
                    rig.Host.Prefs.Settings.Set("screen.layout", layout);
                    if (layout == "single") rig.Resize(420, 640); else rig.Resize(1100, 760);
                    foreach (var preset in new[] { "basic", "mobile", "business", "wire" })
                        foreach (var shape in new[] { "bubble", "card", "compact" })
                            foreach (var theme in Theme.Ids)
                            {
                                rig.Host.Prefs.Settings.Set("screen.preset", preset);
                                rig.Host.Prefs.Settings.Set("screen.chatShape", shape);
                                rig.Host.Prefs.Settings.Set("screen.theme", theme);
                                rig.Step();
                                A.True(ControlOverlaps(rig.Form).Count == 0, layout + "/" + preset + "/" + shape + "/" + theme + ": " + string.Join("; ", ControlOverlaps(rig.Form)));
                                if (theme == "default" || (preset == "basic" && shape == "bubble")) rig.Save(layout + "-" + preset + "-" + shape + "-" + theme);
                                n++;
                            }
                }
                A.Eq(144, n, "all combinations rendered");
            }
        }

        /// <summary>Visible sibling controls in each container must not overlap, and fixed texts must fit their control.</summary>
        public static List<string> ControlOverlaps(Control root)
        {
            var bad = new List<string>();
            Walk(root, bad);
            return bad;
        }

        private static void Walk(Control c, List<string> bad)
        {
            var kids = c.Controls.Cast<Control>().Where(k => k.Visible && k.Width > 0 && k.Height > 0 && !(k is ChatView) && !(k is Splitter)).ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i].Right > c.ClientSize.Width + 1 || kids[i].Bottom > c.ClientSize.Height + 1 || kids[i].Left < 0 || kids[i].Top < 0)
                    if (!(c is Form)) bad.Add(Name(kids[i]) + " outside " + Name(c) + " " + kids[i].Bounds + " in " + c.ClientSize);
                for (int j = i + 1; j < kids.Count; j++)
                {
                    if (kids[i].Dock != DockStyle.None && kids[j].Dock != DockStyle.None) continue; // docking never overlaps
                    if (kids[i].Bounds.IntersectsWith(kids[j].Bounds)) bad.Add(Name(kids[i]) + " overlaps " + Name(kids[j]));
                }
                var b = kids[i] as Button;
                if (b != null && b.Text.Length > 0)
                {
                    var need = TextRenderer.MeasureText(b.Text, b.Font).Width;
                    if (need > b.Width) bad.Add("button text cut: " + b.Text + " needs " + need + " has " + b.Width);
                }
                Walk(kids[i], bad);
            }
        }

        private static string Name(Control c) { return c.GetType().Name + "[" + (c.AccessibleName ?? c.Text) + "]"; }

        [Test("30-8.8")]
        public static void WindowSizes()
        {
            using (var rig = new UiRig("sizes"))
            {
                foreach (var size in new[] { new Size(360, 420), new Size(720, 520), new Size(1920, 1080) })
                {
                    rig.Resize(size.Width, size.Height);
                    rig.Step();
                    var bad = ControlOverlaps(rig.Form);
                    A.True(bad.Count == 0, size + ": " + string.Join("; ", bad));
                    // fixed labels under the input must show their whole text
                    foreach (var l in new Label[] { rig.Form.CounterLabel, rig.Form.HintLabel, rig.Form.StorageLabel })
                        if (l.Visible) A.True(TextRenderer.MeasureText(l.Text, l.Font).Width <= l.Width, size + " label cut: " + l.Text + " (" + TextRenderer.MeasureText(l.Text, l.Font).Width + " > " + l.Width + ")");
                    rig.Save("size-" + size.Width + "x" + size.Height);
                }
            }
        }

        [Test("30-8.1")]
        public static void LayoutSwitchKeepsState()
        {
            using (var rig = new UiRig("switch", 80))
            {
                rig.Resize(1100, 760);
                rig.Host.Prefs.Settings.Set("screen.layout", "single");
                rig.Step();
                rig.Form.Input.Text = "쓰던 글";
                rig.Form.OpenSearch(); rig.Form.SearchBox.Text = "메시지 1";
                var room = rig.Host.Chat.ViewLobbyId;
                rig.Form.Chat.ScrollToItemKey(rig.Form.Chat.Rows[5].Key);
                var anchor = rig.Form.Chat.ScrollOffset;
                A.Eq(LayoutKind.Single, rig.Form.CurrentLayout, "single");
                rig.Host.Prefs.Settings.Set("screen.layout", "sidebar");
                rig.Step();
                A.Eq(LayoutKind.Sidebar, rig.Form.CurrentLayout, "switched without restart");
                A.Eq("쓰던 글", rig.Form.Input.Text, "draft kept");
                A.Eq("메시지 1", rig.Form.SearchBox.Text, "search kept");
                A.Eq(room, rig.Host.Chat.ViewLobbyId, "room kept");
                A.True(rig.Form.Sidebar.Visible, "sidebar shown");
                A.True(Math.Abs(rig.Form.Chat.ScrollOffset - anchor) < 400, "scroll near the same message (" + anchor + " → " + rig.Form.Chat.ScrollOffset + ")");
            }
        }

        [Test("30-8.2")]
        public static void AutoLayoutByWidth()
        {
            using (var rig = new UiRig("auto"))
            {
                rig.Resize(800, 600); rig.Form.ApplyLayoutFromSetting();
                A.Eq(LayoutKind.Sidebar, rig.Form.CurrentLayout, "800 → sidebar");
                rig.Resize(740, 600); rig.Form.ApplyLayoutFromSetting();
                A.Eq(LayoutKind.Sidebar, rig.Form.CurrentLayout, "740 keeps sidebar");
                rig.Resize(700, 600); rig.Form.ApplyLayoutFromSetting();
                A.Eq(LayoutKind.Single, rig.Form.CurrentLayout, "700 → single");
                rig.Resize(740, 600); rig.Form.ApplyLayoutFromSetting();
                A.Eq(LayoutKind.Single, rig.Form.CurrentLayout, "740 keeps single");
            }
        }

        [Test("30-8.3")]
        public static void SidebarCollapseAndWidthSaved()
        {
            using (var rig = new UiRig("collapse"))
            {
                rig.Resize(1100, 760);
                rig.Host.Prefs.Settings.Set("screen.layout", "sidebar"); rig.Step();
                A.True(rig.Form.Sidebar.Visible, "visible");
                rig.Form.ToggleSidebar();
                A.False(rig.Form.Sidebar.Visible, "collapsed");
                A.True(rig.Host.Prefs.Settings.GetBool("screen.sidebarCollapsed"), "saved");
                rig.Form.ToggleSidebar();
                A.True(rig.Form.Sidebar.Visible, "expanded");
                rig.Host.Prefs.Settings.Set("screen.sidebarWidth", 300);
                var again = new NyangTalk.Application.PreferencesService(new NyangTalk.Adapters.Storage.JsonDocumentStore(Path.Combine(rig.Dir, "data")));
                again.Load();
                A.Eq(300, again.Settings.GetInt("screen.sidebarWidth"), "width persisted");
            }
        }

        [Test("30-8.4")]
        public static void ArchivedRoomIsReadOnly()
        {
            using (var rig = new UiRig("archive"))
            {
                var oldLobby = rig.Game.LobbyId;
                rig.Game.LobbyId = "109775240000999999"; rig.Game.LobbyName = "새 로비";
                rig.Step(); rig.Step();
                A.True(rig.Form.ArchivedRooms().Any(r => r.LobbyId == oldLobby), "old room listed");
                rig.Host.Chat.OpenArchived(oldLobby, "데모 로비");
                rig.Step();
                rig.Form.Input.Text = "보관 방에 쓰기";
                rig.Form.UpdateComposerState();
                A.False(rig.Form.SendButton.Enabled, "send disabled");
                A.True(rig.Form.Input.ReadOnly, "input read-only");
                A.True(rig.Form.RoomSub.Text.Contains("보관된 대화"), "archived badge: " + rig.Form.RoomSub.Text);
                A.True(rig.Form.NoticeBand.Visible, "band shown");
            }
        }

        [Test("30-8.5")]
        public static void AutomationAreaHiddenWhenOff()
        {
            using (var rig = new UiRig("autoarea"))
            {
                rig.Resize(1100, 760); rig.Host.Prefs.Settings.Set("screen.layout", "sidebar"); rig.Step();
                A.False(rig.Form.AutomationPanel.Visible, "hidden when nothing is on");
                rig.Form.AutomationStatusText = () => "상자 자동 열기 · 대기 중";
                rig.Step();
                A.True(rig.Form.AutomationPanel.Visible, "shown when something is on");
                A.True(rig.Form.AutomationPanel.Top >= rig.Form.ArchiveList.Bottom && !rig.Form.AutomationPanel.Bounds.IntersectsWith(rig.Form.AppTitle.Bounds), "placed below archive list, never overlapping AppTitle");
            }
        }

        [Test("30-8.6,60-1")]
        public static void FaceStackMaxThree()
        {
            using (var rig = new UiRig("faces"))
            {
                for (int i = 0; i < 5; i++) rig.Game.Members.Add(new[] { "7656119000000010" + i, "추가" + i, "" });
                rig.Step(); rig.Step();
                A.Eq(3, rig.Form.Faces.Faces.Count, "3 faces");
                A.Eq(9, rig.Form.Faces.Count, "member count");
            }
        }

        [Test("50-9.7")]
        public static void ImeEnterDoesNotSend()
        {
            using (var rig = new UiRig("ime"))
            {
                int sent = 0;
                var c = new Composer();
                c.CreateControl();
                c.SendRequested += () => sent++;
                c.Simulate(Composer.WM_IME_STARTCOMPOSITION);
                A.False(c.HandleKey(Keys.Enter, false), "enter while composing");
                A.False(c.HandleKey(Keys.ProcessKey, false), "VK_PROCESSKEY");
                c.Simulate(Composer.WM_IME_ENDCOMPOSITION);
                A.False(c.HandleKey(Keys.Enter, true), "shift+enter is a line break");
                A.True(c.HandleKey(Keys.Enter, false), "plain enter sends");
                A.Eq(1, sent, "sent once");
            }
        }

        [Test("50-9.9")]
        public static void ScrolledUpStaysPut()
        {
            using (var rig = new UiRig("scroll", 120))
            {
                rig.Resize(420, 640);
                var chat = rig.Form.Chat;
                chat.ScrollToBottom();
                chat.ScrollToItemKey(chat.Rows[10].Key);
                rig.Step();
                int before = chat.ScrollOffset;
                var firstKey = chat.Rows[chat.RowAt(before)].Key;
                rig.Game.Say(2, "새 글 1", rig.Clock.Now); rig.Game.Say(2, "새 글 2", rig.Clock.Now);
                rig.Step();
                A.Eq(firstKey, chat.Rows[chat.RowAt(chat.ScrollOffset)].Key, "view not pulled down");
                A.True(chat.NewButtonVisible, "new messages button");
                A.Eq(2, chat.NewMessageCount, "2 new");
                chat.ScrollToBottom();
                A.False(chat.NewButtonVisible, "button gone at bottom");
            }
        }

        [Test("50-9.6,50-5")]
        public static void SearchJumpsToUnpaintedRow()
        {
            using (var rig = new UiRig("search", 600))
            {
                rig.Resize(420, 640);
                rig.Form.Chat.ScrollToBottom();
                rig.Form.OpenSearch();
                rig.Form.SearchBox.Text = "메시지 3";   // matches "메시지 3", "메시지 30".. many
                A.True(rig.Form.Search.Hits.Count > 5, "hits " + rig.Form.Search.Hits.Count);
                rig.Form.SearchStep(true); // wraps to the oldest hit, far above
                int idx = rig.Form.Search.CurrentItemIndex;
                using (rig.Form.Chat.Snapshot()) { }
                A.True(rig.Form.Chat.PaintedRowsLastPaint < 40, "only visible rows painted: " + rig.Form.Chat.PaintedRowsLastPaint);
                int row = rig.Form.Chat.RowAt(rig.Form.Chat.ScrollOffset);
                A.True(row <= idx + 2 && row >= idx - 12, "scrolled to the hit (row " + row + ", item " + idx + ")");
                A.True(rig.Form.Chat.TotalHeight > rig.Form.Chat.ViewportHeight * 10, "long list");
            }
        }

        [Test("10-11.7")]
        public static void SendAreaBrokenLocksOnlyInput()
        {
            using (var rig = new UiRig("sendbroken"))
            {
                rig.Game.Areas["ChatSend"] = false;
                rig.Step();
                rig.Form.Input.Text = "보내기";
                rig.Form.UpdateComposerState();
                A.True(rig.Form.Input.ReadOnly, "input locked");
                A.False(rig.Form.SendButton.Enabled, "send disabled");
                A.True(rig.Form.NoticeBand.Visible && rig.Form.NoticeLabel.Text.Contains("읽기는 계속"), "notice: " + rig.Form.NoticeLabel.Text);
                int before = rig.Host.Chat.Messages.Count;
                rig.Game.Say(2, "받기는 계속", rig.Clock.Now);
                rig.Step();
                A.Eq(before + 1, rig.Host.Chat.Messages.Count, "receiving continues");
                rig.Save("send-broken");
            }
        }

        [Test("60-5.5,70-3.6")]
        public static void DependentSettingsDisabled()
        {
            var dir = Ctx.NewDir("settingsform");
            var prefs = new NyangTalk.Application.PreferencesService(new NyangTalk.Adapters.Storage.JsonDocumentStore(dir));
            prefs.Load();
            using (var f = new SettingsForm(prefs))
            {
                f.CreateControl();
                f.ShowCategory("people");
                prefs.Settings.Set("people.colorMode", "none");
                A.False(f.IsEditorEnabled("people.colorPlacement"), "placement disabled for 색 없음");
                prefs.Settings.Set("people.colorMode", "auto");
                A.True(f.IsEditorEnabled("people.colorPlacement"), "enabled again");
                f.ShowCategory("char");
                prefs.Settings.Set("char.mine", "hide");
                A.False(f.IsEditorEnabled("char.minePosition"), "position disabled when hidden");
                prefs.Settings.Set("char.mine", "show");
                A.True(f.IsEditorEnabled("char.minePosition"), "enabled when shown");
            }
        }

        [Test("30-7.2.4,30-7.2.2")]
        public static void PresetAndShapeIndependent()
        {
            using (var rig = new UiRig("preset", 60))
            {
                rig.Form.Input.Text = "초안";
                rig.Host.Prefs.Settings.Set("screen.chatShape", "card");
                rig.Host.Prefs.Settings.Set("screen.preset", "mobile");
                A.Eq("card", rig.Host.Prefs.Settings.Get("screen.chatShape"), "preset keeps shape");
                rig.Host.Prefs.Settings.Set("screen.chatShape", "compact");
                A.Eq("mobile", rig.Host.Prefs.Settings.Get("screen.preset"), "shape keeps preset");
                rig.Step();
                A.Eq("초안", rig.Form.Input.Text, "draft kept");
                A.True(rig.Form.Chat.Rows.Count > 50, "content kept");
            }
        }

        [Test("90-7.6")]
        public static void LanguageSwitchIsImmediate()
        {
            using (var rig = new UiRig("lang"))
            {
                rig.Host.Prefs.Settings.Set("screen.preset", "basic");
                rig.Host.Prefs.Settings.Set("ui.language", "en");
                A.Eq("Send", rig.Form.SendButton.Text, "english");
                rig.Host.Prefs.Settings.Set("ui.language", "ja");
                A.Eq("送信", rig.Form.SendButton.Text, "japanese");
                A.Eq("NyangTalk " + AppVersion.Text, rig.Form.Text, "title: name and version only");
                rig.Host.Prefs.Settings.Set("ui.language", "ko");
                A.Eq("NyangTalk " + AppVersion.Text, rig.Form.Text, "title shows the version");
            }
        }

        [Test("90-4")]
        public static void EverySettingHasLabels()
        {
            foreach (var d in NyangTalk.Domain.Preferences.Settings.Defs)
            {
                A.True(Strings.Table.ContainsKey("set." + d.Key), "label for " + d.Key);
                if (d.Choices == null) continue;
                foreach (var v in d.Choices)
                    A.True(Strings.ValueLabel(d.Key, v, "ko") != v || Strings.Table.ContainsKey("val." + v), "choice label " + d.Key + "=" + v);
            }
        }

        [Test("50-9.5,90-7.7")]
        public static void FirstDisplayOf3000()
        {
            using (var rig = new UiRig("perf3000", 0))
            {
                var batch = new List<ChatMessage>();
                for (int i = 0; i < 3200; i++) batch.Add(new ChatMessage { Id = "h" + i, SenderId = "7656119000000000" + (i % 5), SenderName = "p" + (i % 5), Body = "기록 메시지 " + i, TimestampUtc = Fake.T0.AddSeconds(i) });
                rig.Host.History.Append(rig.Game.LobbyId, "perf", batch);
                rig.Host.Chat.CloseArchived(); // reload current room from the record
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (rig.Host.Chat.CanShowMore) rig.Host.Chat.ShowMore();
                rig.Form.RefreshChat();
                using (rig.Form.Chat.Snapshot()) { }
                sw.Stop();
                A.True(rig.Form.Chat.Rows.Count >= 3000, "3000 rows");
                GC.Collect();
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                Ctx.Measurements.Add("3,000개 표시(기록 읽기·묶기·배치·첫 그리기): " + sw.ElapsedMilliseconds + " ms (목표 1,500 ms). 검사 프로세스 작업 집합 " + (proc.WorkingSet64 / 1024 / 1024) + " MB, 개인 바이트 " + (proc.PrivateMemorySize64 / 1024 / 1024) + " MB — 검사 실행기 전체 값이라 앱 단독 값이 아님");
                A.True(sw.ElapsedMilliseconds < 1500, "under 1.5 s: " + sw.ElapsedMilliseconds);
            }
        }
    }
}
