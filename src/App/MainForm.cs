using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NyangTalk.App.Controls;
using NyangTalk.App.Platform;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;
using LinkState = NyangTalk.Domain.GameLink.LinkState;

namespace NyangTalk.App
{
    /// <summary>
    /// The main window. Both layouts (30-SPEC) use the same control instances, so switching keeps the room, scroll,
    /// search text and draft by construction; only the sidebar and header arrangement change.
    /// </summary>
    public sealed class MainForm : Form, IMessageFilter
    {
        private readonly AppHost _host;
        private readonly ChatService _chat;
        private readonly PreferencesService _prefs;
        private readonly Timer _timer = new Timer();

        // layout parts
        public readonly Panel Sidebar = new Panel();
        public readonly ScrollArea SidebarArea = new ScrollArea();
        public readonly Splitter SidebarSplitter = new Splitter();
        public readonly Panel Main = new Panel();
        public readonly Panel Header = new Panel();
        public readonly Panel NoticeBand = new Panel();
        public readonly Panel SearchBar = new Panel();
        public readonly Panel ComposerArea = new Panel();
        public readonly ChatView Chat = new ChatView();
        public readonly Composer Input = new Composer();
        public readonly IconButton SendButton = new IconButton(IconKind.Send);
        public readonly Label HintLabel = new Label();
        public readonly Label CounterLabel = new Label();
        public readonly Label StorageLabel = new Label();
        public readonly Label RoomTitle = new Label();
        public readonly Label RoomSub = new Label();
        public readonly FaceStack Faces = new FaceStack();
        public readonly IconButton SearchButton = new IconButton(IconKind.Search);
        public readonly IconButton SettingsButton = new IconButton(IconKind.Gear);
        public readonly IconButton RoomsButton = new IconButton(IconKind.Rooms);
        /// <summary>95-SPEC 2.6: the one notice line (sentence + one action button), connection problems first.</summary>
        public readonly NoticeLine Notices = new NoticeLine();
        public readonly IconButton NoticeAction = new IconButton(IconKind.None);
        public readonly ParticipantsPanel People = new ParticipantsPanel();
        public readonly IconButton MoreButton = new IconButton(IconKind.More);
        /// <summary>Tooltips of every header and sidebar button: ~0.5 s delay, also on keyboard focus (30-SPEC 3장, 5장).</summary>
        public readonly HoverTips Tips = new HoverTips();
        public readonly IconButton ReconnectButton = new IconButton(IconKind.Refresh);
        public readonly Label NoticeLabel = new Label();
        public readonly IconButton NoticeClose = new IconButton(IconKind.Close);
        public readonly TextBox SearchBox = new TextBox();
        public readonly Label SearchCount = new Label();
        public readonly IconButton SearchPrev = new IconButton(IconKind.Up);
        public readonly IconButton SearchNext = new IconButton(IconKind.Down);
        public readonly IconButton SearchCloseButton = new IconButton(IconKind.Close);
        // sidebar parts
        public readonly Label AppTitle = new Label();
        public readonly IconButton CollapseButton = new IconButton(IconKind.ChevronLeft);
        public readonly IconButton ExpandButton = new IconButton(IconKind.ChevronRight);
        public readonly Button GlobalSearchButton = new Button();
        public readonly Label CurrentHeader = new Label();
        public readonly Button CurrentRoomButton = new Button();
        public readonly Label ArchiveHeader = new Label();
        public readonly ThemedList ArchiveList = new ThemedList();
        public readonly Panel AutomationPanel = new Panel();
        public readonly Label AutomationLabel = new Label();
        public readonly Panel SidebarBottom = new Panel();
        public readonly Label MyName = new Label();
        public readonly Label LinkDot = new Label();
        // 95-SPEC 2.9: first-run language screen inside the main window (never a separate popup)
        public readonly Panel FirstRunPanel = new Panel();
        public readonly Label FirstRunTitle = new Label();
        public readonly Label FirstRunNote = new Label();
        public readonly ThemedCheck FirstRunKo = new ThemedCheck { Text = "한국어", Radio = true, Checked = true, AccessibleName = "한국어" };
        public readonly ThemedCheck FirstRunEn = new ThemedCheck { Text = "English", Radio = true, AccessibleName = "English" };
        public readonly ThemedCheck FirstRunJa = new ThemedCheck { Text = "日本語", Radio = true, AccessibleName = "日本語" };
        public readonly Button FirstRunStart = new Button();

        public string SelectedFirstRunLanguage { get { return FirstRunEn.Checked ? "en" : (FirstRunJa.Checked ? "ja" : "ko"); } }

        private LayoutKind _layout = LayoutKind.Single;
        private readonly SearchSession _search = new SearchSession();
        private List<TimelineItem> _items = new List<TimelineItem>();
        private bool _dirty = true;
        public LayoutKind CurrentLayout { get { return _layout; } }
        public SearchSession Search { get { return _search; } }
        public bool SidebarCollapsed { get; private set; }
        public Func<string> AutomationStatusText = () => null;
        public Action TickHook;

        public MainForm(AppHost host)
        {
            _host = host; _chat = host.Chat; _prefs = host.Prefs;
            Text = string.Format(L("app.title"), AppVersion.Text);
            if (_host.Demo != null) Text += " · " + L("demo.title");
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Load();
            Build();
            ApplyStyle();
            ApplyWindowSize(true);
            ApplyLayoutFromSetting();

            _host.Bus.Subscribe<MessageReceived>(e => _dirty = true);
            _host.Bus.Subscribe<ParticipantsChanged>(e => _dirty = true);
            _host.Bus.Subscribe<OutboxChanged>(e => _dirty = true);
            FormClosing += (s, e) => _chat.SaveOutbox();
            _host.Bus.Subscribe<LobbyChanged>(e =>
            {
                _dirty = true; RefreshArchive();
                if (!_chat.IsArchivedView) DeletePortraits(_host.Portraits.LeaveRoom()); // 70-SPEC 2.2: pictures of the old room go
            });
            _host.Bus.Subscribe<LinkStateChanged>(e => UpdateHeader());
            _host.Bus.Subscribe<NoticeRaised>(e => ShowNotice(e.Key, L(e.Text)));
            _prefs.Settings.Changed += k => { if (k == "chat.retentionDays") { try { _host.Exchange.WriteRetention(_prefs.Settings.GetInt(k)); } catch (IOException ex) { _host.AddLog("retention: " + ex.Message); } } };
            _prefs.Changed += () => { ApplyStyle(); ApplyLayoutFromSetting(); _dirty = true; Chat.InvalidateLayout(); };
            _prefs.Settings.Changed += k => { if (k == "screen.windowSize" || k == "screen.layout") ApplyWindowSize(false); };
            ResizeEnd += (s, e) => OnUserResized();
            FormClosing += (s, e) => { if (_prefs.Settings.Get("screen.windowSize") == WindowSizes.Custom && WindowState == FormWindowState.Normal) _prefs.SaveWindowSize("main", DisplayScale.Logical(Width, DeviceDpiSafe), DisplayScale.Logical(Height, DeviceDpiSafe)); };
            _prefs.SaveFailed += key => ShowNotice("save:" + key, L(key));
            if (_prefs.BrokenSettingsCopy != null) ShowNotice("broken-settings", string.Format(L("settings.broken"), Path.GetFileName(_prefs.BrokenSettingsCopy)));

            _timer.Interval = 800; // 0.75–1 s (90-SPEC 6장)
            _timer.Tick += (s, e) => Tick();
            MouseOverWindow = () => Bounds.Contains(Cursor.Position);
            Activated += (s, e) => SetActiveForBorder(true);
            Deactivate += (s, e) => SetActiveForBorder(false);
            Resize += (s, e) => ApplyBorder();
            Activated += (s, e) => { _chat.WindowActive = true; _chat.Unread.OnActivated(Chat.IsAtBottom); UpdateBadges(); };
            Deactivate += (s, e) => { _chat.WindowActive = false; };
            Chat.BottomChanged += atBottom => { _chat.AtBottom = atBottom; _chat.Unread.OnScrolled(_chat.WindowActive && Form.ActiveForm == this, atBottom); UpdateBadges(); };
            Resize += (s, e) => ApplyLayoutFromSetting();
            RefreshArchive();
            System.Windows.Forms.Application.AddMessageFilter(this);
            FormClosed += (s, e) => System.Windows.Forms.Application.RemoveMessageFilter(this);
            Disposed += (s, e) => { System.Windows.Forms.Application.RemoveMessageFilter(this); foreach (var font in _headerPillFonts.Values) font.Dispose(); };
        }

        public bool PreFilterMessage(ref Message m)
        {
            if ((m.Msg == 0x0201 || m.Msg == 0x0204) && (SidebarOverlay || ParticipantsOverlay) && !IsDisposed)
            {
                var c = Control.FromHandle(m.HWnd);
                if (c != null && c.FindForm() == this) CloseOverlaysOutside(Cursor.Position);
            }
            return false;
        }

        public string L(string key) { return _prefs.L(key); }

        /// <summary>"다시 연결": read the game's files again now.</summary>
        public void Reconnect() { _chat.Reconnect(); UpdateHeader(); }

        /// <summary>Another start of the app asked this window to come to the front (95-SPEC 1.1).</summary>
        public void ComeToFront()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Activate();
            BringToFront();
        }

        public void StartTimer() { _timer.Start(); }

        // ---------------------------------------------------------------- build

        private Button Glyph(IconButton b, string glyph, string accessibleKey)
        {
            b.AccessibleName = L(accessibleKey);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Size = new Size(32, 30);
            b.TabStop = true;
            return b;
        }

        private void Build()
        {
            SuspendLayout();
            // header
            Header.Dock = DockStyle.Top; Header.Height = 50; Header.Padding = new Padding(10, 4, 6, 4);
            RoomTitle.AutoEllipsis = true; RoomTitle.Location = new Point(10, 5); RoomTitle.Height = 22; RoomTitle.AccessibleName = "room";
            RoomSub.AutoEllipsis = true; RoomSub.Location = new Point(10, 27); RoomSub.Height = 18;
            Glyph(SearchButton, "🔍", "search.placeholder"); SearchButton.Click += (s, e) => OpenSearch();
            Glyph(SettingsButton, "⚙", "menu.settings"); SettingsButton.Click += (s, e) => OpenSettings();
            Glyph(RoomsButton, "", "tip.header.rooms"); RoomsButton.Click += (s, e) => ShowRoomsMenu(RoomsButton);
            Glyph(MoreButton, "", "tip.header.more"); MoreButton.Click += (s, e) => ShowMoreMenu();
            Glyph(ReconnectButton, "↻", "menu.reconnect"); ReconnectButton.Click += (s, e) => Reconnect();
            Faces.AccessibleName = L("menu.participants"); Faces.Click += (s, e) => ShowParticipants();
            Header.Controls.AddRange(new Control[] { RoomTitle, RoomSub, Faces, RoomsButton, SearchButton, ReconnectButton, SettingsButton, MoreButton });
            Header.Resize += (s, e) => LayoutHeader();

            // notice band
            NoticeBand.Dock = DockStyle.Top; NoticeBand.Height = 30; NoticeBand.Visible = false; NoticeBand.Padding = new Padding(10, 0, 4, 0);
            NoticeLabel.AutoSize = false; NoticeLabel.TextAlign = ContentAlignment.MiddleLeft; NoticeLabel.AutoEllipsis = false;
            NoticeBand.Resize += (s, e) => FitNotice();
            Glyph(NoticeClose, "✕", "search.close");
            NoticeClose.Click += (s, e) => { var cur = Notices.Current; if (cur != null) Notices.Dismiss(cur.Key); };
            NoticeAction.AutoSize = false; NoticeAction.Visible = false; NoticeAction.Padding = new Padding(4, 0, 4, 0);
            NoticeAction.Click += (s, e) => { var cur = Notices.Current; if (cur != null && cur.Action != null) cur.Action(); };
            NoticeBand.Controls.Add(NoticeLabel); NoticeBand.Controls.Add(NoticeAction); NoticeBand.Controls.Add(NoticeClose);
            Notices.Changed += RenderNotice;

            // search bar
            SearchBar.Dock = DockStyle.Top; SearchBar.Height = 34; SearchBar.Visible = false; SearchBar.Padding = new Padding(8, 4, 4, 4);
            SearchBox.Dock = DockStyle.Fill; SearchBox.AccessibleName = L("search.placeholder");
            SearchBox.TextChanged += (s, e) => RunSearch();
            SearchBox.KeyDown += SearchBoxKeyDown;
            SearchCount.Dock = DockStyle.Right; SearchCount.Width = 64; SearchCount.TextAlign = ContentAlignment.MiddleCenter; SearchCount.Text = "0/0";
            SearchCount.TextChanged += (s, e) => SearchCount.Width = Math.Max(64, TextRenderer.MeasureText(SearchCount.Text, SearchCount.Font).Width + 8);
            Glyph(SearchPrev, "▲", "search.prev"); SearchPrev.Dock = DockStyle.Right; SearchPrev.Click += (s, e) => SearchStep(false);
            Glyph(SearchNext, "▼", "search.next"); SearchNext.Dock = DockStyle.Right; SearchNext.Click += (s, e) => SearchStep(true);
            Glyph(SearchCloseButton, "✕", "search.close"); SearchCloseButton.Dock = DockStyle.Right; SearchCloseButton.Click += (s, e) => CloseSearch();
            SearchBar.Controls.Add(SearchBox); SearchBar.Controls.Add(SearchCount); SearchBar.Controls.Add(SearchPrev); SearchBar.Controls.Add(SearchNext); SearchBar.Controls.Add(SearchCloseButton);

            // composer
            ComposerArea.Dock = DockStyle.Bottom; ComposerArea.Height = 96; ComposerArea.Padding = new Padding(10, 8, 10, 6);
            Input.AccessibleName = L("send.button");
            Input.SendRequested += DoSend;
            Input.TextChanged += (s, e) => UpdateComposerState();
            SendButton.Text = L("send.button"); SendButton.AccessibleName = L("send.button"); SendButton.FlatStyle = FlatStyle.Flat; SendButton.Click += (s, e) => DoSend();
            SendButton.EnabledChanged += (s, e) => PaintSendButton();
            HintLabel.AutoEllipsis = true; CounterLabel.TextAlign = ContentAlignment.MiddleLeft; StorageLabel.TextAlign = ContentAlignment.MiddleRight; StorageLabel.AutoEllipsis = true;
            ComposerArea.Controls.AddRange(new Control[] { Input, SendButton, CounterLabel, HintLabel, StorageLabel });
            ComposerArea.Resize += (s, e) => LayoutComposer();
            ComposerArea.Paint += PaintComposerFrame;

            Chat.Dock = DockStyle.Fill;
            Chat.ActionClicked += OnChatAction;
            Chat.PersonMenu += OnPersonMenu;
            Chat.Portrait = PortraitFor;
            Chat.Picture = PictureAt;
            Chat.Layout2.EmoteOf = id => { EmoteInfo e; var st = _chat.Status; return st != null && st.Emotes.TryGetValue(id ?? "", out e) ? e : null; };
            Chat.EmoteSalt = eg => string.Join(",", eg.Entries.Select(e => { var i = Chat.Layout2.EmoteOf(e.EmoteId); return i == null ? "-" : (i.File != null && File.Exists(i.File) ? "p" : "n") + (i.Name ?? ""); }));

            Main.Dock = DockStyle.Fill;
            Main.Controls.Add(Chat);
            Main.Controls.Add(ComposerArea);
            Main.Controls.Add(People);   // docks right under the header bands, beside chat and composer
            People.CloseButton.Click += (s, e) => CloseParticipants();
            People.PersonMenu += (p, at) =>
            {
                if (_menu != null) _menu.Dispose();
                var pal = Palette.From(_prefsStyle.Theme);
                _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(pal), BackColor = pal.Panel, ForeColor = pal.Text, Font = Font };
                AddPersonItems(_menu, p.SteamId, p.Name);
                _menu.Show(People.List, at);
            };
            Main.Resize += (s, e) => PlaceParticipants();
            Main.Controls.Add(SearchBar);
            Main.Controls.Add(NoticeBand);
            Main.Controls.Add(Header);

            // sidebar
            Sidebar.Dock = DockStyle.Left; Sidebar.Width = _prefs.Settings.GetInt("screen.sidebarWidth"); Sidebar.Padding = new Padding(10, 8, 8, 8);
            SidebarSplitter.Dock = DockStyle.Left; SidebarSplitter.MinSize = 200; SidebarSplitter.MinExtra = 360; SidebarSplitter.Width = 4;
            SidebarSplitter.SplitterMoved += (s, e) =>
            {
                int w = Math.Max(200, Math.Min(320, DisplayScale.Logical(Sidebar.Width, DeviceDpiSafe)));
                _prefs.Settings.Set("screen.sidebarWidth", w);
                Sidebar.Width = DisplayScale.Pixels(w, DeviceDpiSafe);
            };
            AppTitle.Text = "NyangTalk"; AppTitle.Font = Fonts.Create("Pretendard", 12f, FontStyle.Bold); AppTitle.AutoSize = false; AppTitle.Height = 28;
            Glyph(CollapseButton, "⟨", "menu.sidebarToggle"); CollapseButton.Click += (s, e) => ToggleSidebar();
            Glyph(ExpandButton, "⟩", "menu.sidebarToggle"); ExpandButton.Click += (s, e) => ToggleSidebar(); ExpandButton.Visible = false;
            GlobalSearchButton.Text = L("search.global"); GlobalSearchButton.AccessibleName = L("search.global"); GlobalSearchButton.FlatStyle = FlatStyle.Flat; GlobalSearchButton.TextAlign = ContentAlignment.MiddleLeft; GlobalSearchButton.Click += (s, e) => OpenGlobalSearch();
            CurrentHeader.Text = L("room.current"); CurrentHeader.Height = 22;
            CurrentRoomButton.FlatStyle = FlatStyle.Flat; CurrentRoomButton.TextAlign = ContentAlignment.MiddleLeft; CurrentRoomButton.AutoEllipsis = false; CurrentRoomButton.Click += (s, e) => { if (_chat.IsArchivedView) _chat.CloseArchived(); };
            CurrentRoomButton.TextChanged += (s, e) => LayoutSidebar();
            ArchiveHeader.Text = L("room.archived"); ArchiveHeader.Height = 22;
            ArchiveList.AccessibleName = L("room.archived");
            ArchiveList.DoubleClick += (s, e) => OpenSelectedArchive();
            ArchiveList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OpenSelectedArchive(); if (e.KeyCode == Keys.Delete) DeleteSelectedArchive(); };
            ArchiveList.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ArchiveMenu(e.Location); else OpenSelectedArchive(); };
            AutomationPanel.Height = 44; AutomationPanel.Visible = false; AutomationLabel.Dock = DockStyle.Fill; AutomationLabel.Cursor = Cursors.Hand;
            AutomationLabel.Click += (s, e) => OpenSettings("auto");
            AutomationLabel.TextChanged += (s, e) => LayoutSidebar();
            AutomationPanel.Controls.Add(AutomationLabel);
            SidebarBottom.Height = 56;
            MyName.AutoEllipsis = true; LinkDot.AutoSize = false; LinkDot.Text = "●";
            SidebarBottom.Controls.AddRange(new Control[] { MyName, LinkDot });
            SidebarBottom.Resize += (s, e) => LayoutSidebarBottom();
            var sidebarControls = new Control[] { AppTitle, CollapseButton, GlobalSearchButton, CurrentHeader, CurrentRoomButton, ArchiveHeader, ArchiveList, AutomationPanel, SidebarBottom };
            SidebarArea.Dock = DockStyle.Fill;
            SidebarArea.Content.Controls.AddRange(sidebarControls);
            Sidebar.Controls.Add(SidebarArea);
            foreach (var control in sidebarControls) control.Enter += (s, e) => SidebarArea.EnsureVisible((Control)s);
            SidebarArea.Resize += (s, e) => LayoutSidebar();
            Sidebar.Resize += (s, e) => LayoutSidebar();

            Controls.Add(Main);
            Controls.Add(SidebarSplitter);
            Controls.Add(Sidebar);
            Header.Controls.Add(ExpandButton);   // part of the header row, the title starts after it

            // 95-SPEC 2.9: first-run language chooser inside the app window, Korean pre-selected
            FirstRunPanel.Dock = DockStyle.Fill;
            FirstRunPanel.Visible = _prefs.FirstRun;
            FirstRunTitle.AutoSize = false; FirstRunTitle.TextAlign = ContentAlignment.MiddleCenter;
            FirstRunNote.AutoSize = false; FirstRunNote.TextAlign = ContentAlignment.MiddleCenter;
            Action<string> pickLang = code =>
            {
                FirstRunKo.Checked = code == "ko";
                FirstRunEn.Checked = code == "en";
                FirstRunJa.Checked = code == "ja";
                UpdateFirstRunTexts();
            };
            FirstRunKo.Click += (s, e) => pickLang("ko");
            FirstRunEn.Click += (s, e) => pickLang("en");
            FirstRunJa.Click += (s, e) => pickLang("ja");
            FirstRunKo.CheckedChanged += (s, e) => { if (FirstRunKo.Checked) pickLang("ko"); };
            FirstRunEn.CheckedChanged += (s, e) => { if (FirstRunEn.Checked) pickLang("en"); };
            FirstRunJa.CheckedChanged += (s, e) => { if (FirstRunJa.Checked) pickLang("ja"); };
            FirstRunStart.FlatStyle = FlatStyle.Flat;
            FirstRunStart.Click += (s, e) => ChooseFirstRunLanguage(SelectedFirstRunLanguage);
            FirstRunPanel.Controls.AddRange(new Control[] { FirstRunTitle, FirstRunKo, FirstRunEn, FirstRunJa, FirstRunStart, FirstRunNote });
            FirstRunPanel.Resize += (s, e) => LayoutFirstRun();
            UpdateFirstRunTexts();
            Controls.Add(FirstRunPanel);
            FirstRunPanel.BringToFront();
            ResumeLayout(true);
        }

        private void UpdateFirstRunTexts()
        {
            var lang = SelectedFirstRunLanguage;
            FirstRunTitle.Text = Strings.Get("firstRun.title", lang);
            FirstRunNote.Text = Strings.Get("firstRun.note", lang);
            FirstRunStart.Text = Strings.Get("firstRun.start", lang);
            FirstRunStart.AccessibleName = FirstRunStart.Text;
        }

        private void LayoutFirstRun()
        {
            int w = Math.Min(360, Math.Max(220, FirstRunPanel.ClientSize.Width - 32));
            int x = (FirstRunPanel.ClientSize.Width - w) / 2;
            int y = Math.Max(24, (FirstRunPanel.ClientSize.Height - 250) / 2);
            FirstRunTitle.SetBounds(16, y, Math.Max(200, FirstRunPanel.ClientSize.Width - 32), 44);
            y += 54;
            foreach (var r in new[] { FirstRunKo, FirstRunEn, FirstRunJa })
            {
                r.SetBounds(x + (w - 160) / 2, y, 160, 30);
                y += 34;
            }
            y += 8;
            FirstRunStart.SetBounds(x + (w - 140) / 2, y, 140, 34);
            y += 42;
            FirstRunNote.SetBounds(16, y, Math.Max(200, FirstRunPanel.ClientSize.Width - 32), 36);
        }

        /// <summary>95-SPEC 2.9: selects and saves the language on the first-run screen and enters the main view in that language.</summary>
        public void ChooseFirstRunLanguage(string lang)
        {
            FirstRunKo.Checked = lang == "ko";
            FirstRunEn.Checked = lang == "en";
            FirstRunJa.Checked = lang == "ja";
            _prefs.CompleteFirstRun(SelectedFirstRunLanguage);
            FirstRunPanel.Visible = false;
            ApplyStyle();
            UpdateHeader();
            RefreshArchive();
            RefreshChat();
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Header icons hidden into "더 보기(…)" because the window is too narrow (mini only).</summary>
        public readonly List<Control> Overflow = new List<Control>();
        public const int MinTitleWidth = 110;

        /// <summary>
        /// 30-SPEC 3장: icons in a row (saved rooms, search, reconnect, settings) instead of a ≡ menu. When the window is
        /// too narrow (90-SPEC 1.5 mini) the less used ones go into one "…" button, least used first.
        /// </summary>
        private void LayoutHeader()
        {
            bool sidebar = _layout == LayoutKind.Sidebar;
            bool pill = _prefs.Settings.Get("screen.headerButtons") == "pill";
            var th = _prefsStyle != null ? _prefsStyle.Theme : Theme.Get("default", false);
            bool narrow = Width < 420;
            int gap = pill ? (narrow ? HeaderPill.NarrowGap : HeaderPill.WideGap) : 2;
            int edge = pill ? 14 : 6;
            var pillPairs = new[]
            {
                new KeyValuePair<IconButton, string>(RoomsButton, "header.btn.rooms"),
                new KeyValuePair<IconButton, string>(SearchButton, "header.btn.search"),
                new KeyValuePair<IconButton, string>(ReconnectButton, "header.btn.reconnect"),
                new KeyValuePair<IconButton, string>(SettingsButton, "header.btn.settings"),
                new KeyValuePair<IconButton, string>(MoreButton, "header.btn.more")
            };
            foreach (var kv in pillPairs)
            {
                var b = kv.Key;
                b.Pill = pill;
                b.PillText = pill ? L(kv.Value) : null;
                b.Font = pill ? (_headerPillFont ?? Font) : Font;
                b.ForeColor = RenderStyle.C(th.MidText);
                b.PillFill = b.PillHover = b.PillDown = RenderStyle.C(th.OtherBubble);
                b.PillBorder = RenderStyle.C(th.Border);
                int iconSide = Math.Max(30, Font.Height + 10);
                b.Size = pill ? new Size(HeaderPill.Width(b.PillText, b.Font, narrow), HeaderPill.HeightFor(b.Font)) : new Size(iconSide + 2, iconSide);
                b.Invalidate();
            }
            Faces.Font = _headerPillFont ?? Font;
            Faces.Size = new Size(Faces.PreferredWidth, HeaderPill.HeightFor(Faces.Font));
            var all = sidebar ? new Control[] { SearchButton, ReconnectButton, SettingsButton } : new Control[] { RoomsButton, SearchButton, ReconnectButton, SettingsButton };
            var hideOrder = sidebar ? new Control[] { Faces, ReconnectButton, SearchButton } : (pill ? new Control[] { ReconnectButton, RoomsButton, Faces, SearchButton } : new Control[] { ReconnectButton, RoomsButton, Faces });
            int titleLeft = ExpandButton.Visible ? 44 : 10;
            int width = Header.ClientSize.Width;
            Overflow.Clear();
            Func<int> titleRoom = () =>
            {
                int used = edge + all.Where(b => !Overflow.Contains(b)).Sum(b => b.Width + gap)
                    + (Overflow.Count > 0 ? MoreButton.Width + gap : 0) + (!Overflow.Contains(Faces) ? Faces.Width + gap : 0);
                return width - used - titleLeft - 6;
            };
            foreach (var c in hideOrder) { if (titleRoom() >= MinTitleWidth) break; Overflow.Add(c); }
            foreach (var b in new Control[] { RoomsButton, SearchButton, ReconnectButton, SettingsButton }) b.Visible = all.Contains(b) && !Overflow.Contains(b);
            MoreButton.Visible = Overflow.Count > 0;
            Faces.Visible = !Overflow.Contains(Faces);
            int right = width - edge;
            foreach (var b in new Control[] { SettingsButton }
                .Concat(MoreButton.Visible ? new Control[] { MoreButton } : new Control[0])
                .Concat(all.Reverse().Where(b => b != SettingsButton && b.Visible)))
            {
                right -= b.Width; b.Location = new Point(right, (Header.ClientSize.Height - b.Height) / 2); right -= gap;
            }
            if (Faces.Visible) { right -= Faces.Width + gap; Faces.Location = new Point(Math.Max(10, right), (Header.ClientSize.Height - Faces.Height) / 2); }
            int titleWidth = Math.Max(40, right - titleLeft - 6);
            bool centered = _prefsStyle != null && _prefsStyle.Preset == DesignPreset.Mobile;
            int titleH = RoomTitle.Font.Height + 2, subH = RoomSub.Font.Height + 2;
            int top = Math.Max(2, (Header.ClientSize.Height - titleH - subH) / 2);
            RoomTitle.SetBounds(titleLeft, top, titleWidth, titleH);
            RoomSub.SetBounds(titleLeft, top + titleH, titleWidth, subH);
            RoomTitle.TextAlign = centered ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            RoomSub.TextAlign = RoomTitle.TextAlign;
        }

        private void LayoutComposer()
        {
            var a = ComposerArea.ClientRectangle;
            int pad = 10;
            bool round = _prefsStyle != null && _prefsStyle.Preset == DesignPreset.Mobile;
            int sendW = round ? 44 : Math.Max(72, TextLayout.MeasureText(L("send.button"), SendButton.Font,
                Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 24);
            int footerH = Math.Max(18, CounterLabel.Font.Height + 4);
            int inputH = a.Height - 8 - footerH - 6;
            int roundSide = Math.Max(44, Font.Height + 20);
            if (round) sendW = roundSide;
            SendButton.SetBounds(a.Width - pad - sendW, round ? 8 + (inputH - roundSide) / 2 : 8, sendW, round ? roundSide : inputH);
            Input.SetBounds(pad + 6, 12, Math.Max(40, SendButton.Left - pad - 18), inputH - 8);
            int lineY = a.Height - footerH - 4;
            int counterW = TextRenderer.MeasureText(CounterLabel.Text.Length > 0 ? CounterLabel.Text : "000/000", CounterLabel.Font).Width + 4;
            int hintW = TextRenderer.MeasureText(HintLabel.Text, HintLabel.Font).Width + 2;
            int storeW = TextRenderer.MeasureText(StorageLabel.Text, StorageLabel.Font).Width + 2;
            CounterLabel.SetBounds(pad, lineY, counterW, footerH);
            // The storage note belongs to the sidebar layout (30-SPEC 4장); it is dropped when it would cut the hint.
            bool storage = _layout == LayoutKind.Sidebar && counterW + hintW + storeW + 2 * pad + 16 <= a.Width;
            StorageLabel.Visible = storage;
            StorageLabel.SetBounds(a.Width - pad - storeW, lineY, storeW, footerH);
            int hintRight = storage ? StorageLabel.Left - 8 : a.Width - pad;
            HintLabel.SetBounds(CounterLabel.Right + 4, lineY, Math.Max(10, hintRight - CounterLabel.Right - 4), footerH);
            ComposerArea.Invalidate();
        }

        private void PaintComposerFrame(object sender, PaintEventArgs e)
        {
            if (_prefsStyle == null) return;
            var r = new Rectangle(Input.Left - 6, Input.Top - 4, Input.Width + 12, Input.Height + 8);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = ChatPainter.Rounded(r, _prefsStyle.InputRadius))
            {
                using (var b = new SolidBrush(Input.BackColor)) e.Graphics.FillPath(b, path);
                using (var p = new Pen(RenderStyle.C(_prefsStyle.Theme.Border))) e.Graphics.DrawPath(p, path);
            }
        }

        private bool _layingOutSidebar;
        private void LayoutSidebar()
        {
            if (_layingOutSidebar) return;
            _layingOutSidebar = true;
            try
            {
            int dpi = DeviceDpiSafe;
            Func<int, int> px = v => DisplayScale.Pixels(v, dpi);
            Sidebar.Padding = new Padding(px(10), px(8), px(8), px(8));
            int w = SidebarArea.ContentWidth;
            if (SidebarArea.ClientSize.Width <= 0) return;
            int x = 0, y = 0;
            int gap = px(8);
            int collapseSide = Math.Max(px(28), CollapseButton.Font.Height + px(6));
            int titleH = Math.Max(AppTitle.Font.Height + px(6), collapseSide);
            AppTitle.SetBounds(x, y, Math.Max(1, w - collapseSide - gap), titleH);
            CollapseButton.SetBounds(x + w - collapseSide, y + (titleH - collapseSide) / 2, collapseSide, collapseSide);
            y += titleH + gap;
            int searchH = Math.Max(px(32), GlobalSearchButton.Font.Height + px(12));
            GlobalSearchButton.SetBounds(x, y, w, searchH); y += searchH + px(12);
            int headingH = Math.Max(CurrentHeader.Font.Height, ArchiveHeader.Font.Height) + px(4);
            CurrentHeader.SetBounds(x, y, w, headingH); y += headingH + px(4);
            int roomH = Math.Max(CurrentRoomButton.Font.Height * 2,
                TextRenderer.MeasureText(CurrentRoomButton.Text ?? "", CurrentRoomButton.Font,
                    new Size(Math.Max(1, w - px(16)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height) + px(16);
            CurrentRoomButton.SetBounds(x, y, w, roomH); y += roomH + px(12);
            ArchiveHeader.SetBounds(x, y, w, headingH); y += headingH + px(4);
            int footerH = MyName.Font.Height + LinkDot.Font.Height + px(18);
            int autoH = _showAutomation ? Math.Max(px(36), TextRenderer.MeasureText(AutomationLabel.Text ?? "", AutomationLabel.Font,
                new Size(Math.Max(1, w - px(12)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + px(12)) : 0;
            int contentH = Math.Max(SidebarArea.ClientSize.Height, y + px(48) + gap + footerH + (autoH > 0 ? autoH + gap : 0));
            SidebarArea.SetContentHeight(contentH);
            int bottom = contentH;
            SidebarBottom.SetBounds(x, bottom - footerH, w, footerH);
            LayoutSidebarBottom();
            bottom = SidebarBottom.Top - gap;
            if (_showAutomation)
            {
                AutomationPanel.SetBounds(x, bottom - autoH, w, autoH);
                bottom = AutomationPanel.Top - gap;
            }
            ArchiveList.SetBounds(x, y, w, Math.Max(0, bottom - y));
            }
            finally { _layingOutSidebar = false; }
        }

        private void LayoutSidebarBottom()
        {
            int w = SidebarBottom.ClientSize.Width;
            int gap = DisplayScale.Pixels(6, DeviceDpiSafe);
            MyName.SetBounds(0, gap, w, MyName.Font.Height + gap);
            LinkDot.SetBounds(0, MyName.Bottom + gap, w, LinkDot.Font.Height);
        }

        public void ApplyLayoutFromSetting()
        {
            var setting = _prefs.Settings.Get("screen.layout");
            int logicalWidth = DisplayScale.Logical(ClientSize.Width, DeviceDpiSafe);
            var next = LayoutResolver.Resolve(setting, logicalWidth, _layout);
            bool force = LayoutResolver.ForceCollapse(setting, logicalWidth);
            SetLayout(next, force || _prefs.Settings.GetBool("screen.sidebarCollapsed"));
        }

        private void SetLayout(LayoutKind kind, bool collapsed)
        {
            bool changed = kind != _layout;
            _layout = kind;
            SidebarCollapsed = kind == LayoutKind.Sidebar && collapsed;
            bool showSidebar = kind == LayoutKind.Sidebar && !SidebarCollapsed;
            Sidebar.Width = DisplayScale.Pixels(_prefs.Settings.GetInt("screen.sidebarWidth"), DeviceDpiSafe);
            SidebarSplitter.MinSize = DisplayScale.Pixels(200, DeviceDpiSafe);
            SidebarSplitter.MinExtra = DisplayScale.Pixels(360, DeviceDpiSafe);
            SidebarSplitter.Width = DisplayScale.Pixels(4, DeviceDpiSafe);
            if (SidebarOverlay) CloseSidebarOverlay();
            Sidebar.Visible = showSidebar; SidebarSplitter.Visible = showSidebar;
            // narrow windows can still open the sidebar: over the chat (2026-11-20 user request)
            ExpandButton.Visible = kind == LayoutKind.Single || SidebarCollapsed;
            ExpandButton.Location = new Point(4, Math.Max(4, (Header.Height - ExpandButton.Height) / 2)); ExpandButton.BringToFront();
            MinimumSize = kind == LayoutKind.Sidebar && !collapsed ? SizeFromClientSize(DisplayScale.Size(720, 520, DeviceDpiSafe)) : DisplayScale.Size(300, 360, DeviceDpiSafe);
            if (changed) CloseTransientPopups();
            PerformLayout();
            LayoutSidebar();
            LayoutHeader();
            LayoutComposer();
            UpdateAutomationPanel();
        }

        /// <summary>Popups close when the layout changes (30-SPEC 2장).</summary>
        private void CloseTransientPopups()
        {
            foreach (var f in System.Windows.Forms.Application.OpenForms.Cast<Form>().ToList()) if (f.Owner == this && f.Tag as string == "popup") f.Close();
            if (_menu != null) _menu.Close();
        }

        public void ToggleSidebar()
        {
            bool forced = _layout == LayoutKind.Sidebar && LayoutResolver.ForceCollapse(_prefs.Settings.Get("screen.layout"), DisplayScale.Logical(ClientSize.Width, DeviceDpiSafe));
            if (_layout == LayoutKind.Single || forced)
            {
                if (SidebarOverlay) CloseSidebarOverlay(); else OpenSidebarOverlay();
                return;
            }
            _prefs.Settings.Set("screen.sidebarCollapsed", !_prefs.Settings.GetBool("screen.sidebarCollapsed"));
            ApplyLayoutFromSetting();
        }

        // ---------------------------------------------------------------- overlays (narrow window sidebar, participants)

        /// <summary>The sidebar opened over the chat in a narrow window; closes with Esc, the ⟨ button or a click outside.</summary>
        public bool SidebarOverlay { get; private set; }

        public void OpenSidebarOverlay()
        {
            SidebarOverlay = true;
            Sidebar.Dock = DockStyle.None;
            Sidebar.SetBounds(0, 0, Math.Min(DisplayScale.Pixels(_prefs.Settings.GetInt("screen.sidebarWidth"), DeviceDpiSafe),
                Math.Max(DisplayScale.Pixels(200, DeviceDpiSafe), ClientSize.Width - DisplayScale.Pixels(40, DeviceDpiSafe))), ClientSize.Height);
            LayoutSidebar();
            Sidebar.Visible = true;
            Sidebar.BringToFront();
            ExpandButton.Visible = false;
            CollapseButton.Focus();
        }

        public void CloseSidebarOverlay()
        {
            if (!SidebarOverlay) return;
            SidebarOverlay = false;
            Sidebar.Visible = false;
            Sidebar.Dock = DockStyle.Left;
            Sidebar.SendToBack();
            ExpandButton.Visible = _layout == LayoutKind.Single || SidebarCollapsed;
            ExpandButton.BringToFront();
        }

        public bool ParticipantsOpen { get { return People.Visible; } }
        public const int ParticipantsDockMin = 560;   // narrower than this, the panel opens over the chat

        public void OpenParticipants()
        {
            RefreshParticipants();
            People.Visible = true;
            PlaceParticipants();
            Faces.Pressed = true;
            People.List.Focus();
        }

        public void CloseParticipants()
        {
            People.Visible = false;
            Faces.Pressed = false;
            PlaceParticipants();
        }

        public void PlaceParticipants()
        {
            if (!People.Visible) { People.Dock = DockStyle.Right; return; }
            int panelWidth = DisplayScale.Pixels(ParticipantsPanel.PanelWidth, DeviceDpiSafe);
            if (DisplayScale.Logical(Main.ClientSize.Width, DeviceDpiSafe) < ParticipantsDockMin)
            {
                People.Dock = DockStyle.None;
                int top = Math.Max(Header.Bottom, Math.Max(NoticeBand.Visible ? NoticeBand.Bottom : 0, SearchBar.Visible ? SearchBar.Bottom : 0));
                People.SetBounds(Math.Max(0, Main.ClientSize.Width - panelWidth), top, Math.Min(panelWidth, Main.ClientSize.Width), Math.Max(0, Main.ClientSize.Height - top));
                People.BringToFront();
            }
            else
            {
                People.Dock = DockStyle.Right;
                People.Width = panelWidth;
                Main.Controls.SetChildIndex(People, Main.Controls.GetChildIndex(ComposerArea) + 1);
            }
        }

        public bool ParticipantsOverlay { get { return People.Visible && People.Dock == DockStyle.None; } }

        public void RefreshParticipants()
        {
            var st = _chat.Status;
            var people = _chat.PeopleList();
            int count = st != null && st.Lobby != null && st.Lobby.MemberCount > 0 ? st.Lobby.MemberCount : people.Count;
            bool stale = _chat.Link != LinkState.Connected;
            string note = null;
            if (stale)
            {
                var when = (_chat.LastParticipantsUtc ?? (st != null && st.WrittenUtc > DateTime.MinValue ? st.WrittenUtc : _host.Clock.UtcNow)).ToLocalTime().ToString("HH:mm");
                note = string.Format(L("people.lastSeen"), when);
            }
            else if (!_chat.ParticipantsReadable) note = L("people.unreadable");
            People.SetPeople(people, p => DisplayName.Format(p.Name, _prefs.Aliases.Get(p.SteamId), _prefs.NameMode) + (p.IsOwner ? " · " + L("people.owner") : "") + (p.IsMe ? " · " + L("people.me") : ""),
                string.Format(L("people.panelTitle"), count), note, stale);
            People.Hint.Text = L("people.panelHint");
        }

        /// <summary>A click outside an open overlay closes it (it does not consume the click).</summary>
        public void CloseOverlaysOutside(Point screen)
        {
            if (SidebarOverlay && !Sidebar.RectangleToScreen(Sidebar.ClientRectangle).Contains(screen) && !ExpandButton.RectangleToScreen(ExpandButton.ClientRectangle).Contains(screen)) CloseSidebarOverlay();
            if (ParticipantsOverlay && !People.RectangleToScreen(People.ClientRectangle).Contains(screen) && !Faces.RectangleToScreen(Faces.ClientRectangle).Contains(screen)) CloseParticipants();
        }

        // ---------------------------------------------------------------- style

        private RenderStyle _prefsStyle;
        private Font _headerPillFont;
        private readonly Dictionary<string, Font> _headerPillFonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private Font HeaderFontFor(string family, float bodyPoints)
        {
            Font font;
            var key = family + "|" + bodyPoints;
            if (!_headerPillFonts.TryGetValue(key, out font))
            {
                font = Fonts.Create(family, HeaderPill.FontPoints(bodyPoints));
                _headerPillFonts.Add(key, font);
            }
            return font;
        }

        public void ApplyStyle()
        {
            var s = RenderStyle.From(_prefs.Settings, _prefs.Colors, _prefs.NameMode);
            _prefsStyle = s;
            var th = s.Theme;
            Font = Fonts.Create(s.FontFamily, s.FontSize);
            // Font setters may retain an equal existing instance; keep shared header fonts alive until the form closes.
            _headerPillFont = HeaderFontFor(s.FontFamily, s.FontSize);
            AppTitle.Font = Fonts.Create(s.FontFamily, 12f, FontStyle.Bold);
            BackColor = RenderStyle.C(th.Window);
            ForeColor = RenderStyle.C(th.Text);
            Header.BackColor = s.Preset == DesignPreset.Wire ? RenderStyle.C(th.Window) : RenderStyle.C(th.Panel);
            Sidebar.BackColor = RenderStyle.C(th.Panel);
            var sidebarPalette = Palette.From(th); sidebarPalette.Back = sidebarPalette.Panel;
            SidebarArea.Pal = sidebarPalette;
            SidebarSplitter.BackColor = RenderStyle.C(th.Panel);   // the width handle blends with the sidebar and header (no seam)
            ArchiveList.Pal = Palette.From(th); ArchiveList.Font = Font;
            NoticeBand.BackColor = RenderStyle.C(th.Soft); NoticeLabel.ForeColor = RenderStyle.C(th.Text);
            NoticeLabel.Font = Fonts.Create(s.FontFamily, Math.Max(9f, s.FontSize - 1));
            SearchBar.BackColor = RenderStyle.C(th.Soft);
            SearchBar.Height = Math.Max(34, SearchBox.PreferredHeight + SearchBar.Padding.Vertical);
            SearchBox.BackColor = RenderStyle.C(th.Soft); SearchBox.ForeColor = RenderStyle.C(th.Text); SearchBox.BorderStyle = BorderStyle.FixedSingle;
            SearchCount.BackColor = SearchBar.BackColor; SearchCount.ForeColor = RenderStyle.C(th.SubText);
            SearchCount.Width = Math.Max(64, TextRenderer.MeasureText(SearchCount.Text, SearchCount.Font).Width + 8);
            ComposerArea.BackColor = RenderStyle.C(th.Panel);
            Input.BackColor = RenderStyle.C(th.OtherBubble); Input.ForeColor = RenderStyle.C(th.OtherText);
            if (s.Preset == DesignPreset.Wire) Input.BackColor = RenderStyle.C(th.Window);
            PaintSendButton();
            SendButton.FlatAppearance.BorderSize = s.Preset == DesignPreset.Wire ? 1 : 0;
            SendButton.Round = s.Preset == DesignPreset.Mobile;
            SendButton.Text = s.Preset == DesignPreset.Mobile ? "" : L("send.button");
            foreach (var ib in new[] { SearchButton, SettingsButton, RoomsButton, MoreButton, ReconnectButton, NoticeClose, SearchPrev, SearchNext, SearchCloseButton, CollapseButton, ExpandButton })
                ib.IconColor = RenderStyle.C(th.Text);
            NoticeClose.IconColor = RenderStyle.C(th.MidText);
            foreach (var l in new Control[] { RoomSub, HintLabel, CounterLabel, StorageLabel, CurrentHeader, ArchiveHeader, AutomationLabel }) l.ForeColor = RenderStyle.C(th.SubText);
            AutomationPanel.BackColor = Sidebar.BackColor; AutomationLabel.BackColor = Sidebar.BackColor;
            // 30-SPEC 3장: the status line one step smaller than the body (9–10 pt) and smaller than the room name,
            // in the sub-text colour that keeps 4.5:1 on the header (checked for every theme, light and dark)
            float subPt = StatusLinePoints(s.FontSize);
            RoomSub.Font = Fonts.Create(s.FontFamily, subPt);
            foreach (var label in new[] { HintLabel, CounterLabel, StorageLabel }) label.Font = RoomSub.Font;
            ComposerArea.Height = Math.Max(96, Font.Height * 2 + CounterLabel.Font.Height + 36);
            RoomTitle.Font = Fonts.Create(s.FontFamily, Math.Max(s.FontSize + 1, subPt + 2), FontStyle.Bold);
            RoomTitle.ForeColor = RenderStyle.C(th.Text);
            RoomSub.BackColor = Header.BackColor; RoomTitle.BackColor = Header.BackColor;
            Header.Height = Math.Max(50, RoomTitle.Font.Height + RoomSub.Font.Height + 14);
            SetTips();
            CurrentRoomButton.FlatAppearance.BorderColor = RenderStyle.C(th.Border);
            GlobalSearchButton.FlatAppearance.BorderColor = RenderStyle.C(th.Border);
            TopMost = _prefs.Settings.GetBool("screen.topmost");
            ApplyOpacity();
            ApplyBorder();
            FormBorderStyle = FormBorderStyle.Sizable;
            Chat.Style = s;
            Chat.Layout2.AliasOf = id => _prefs.Aliases.Get(id);
            Chat.Layout2.AskingResend = id => _askResend.Contains(id);
            Faces.Style = s; Faces.AliasOf = id => _prefs.Aliases.Get(id);
            HintLabel.Text = L("send.hint");
            StorageLabel.Text = L("send.storage");
            Text = string.Format(L("app.title"), AppVersion.Text);
            if (_host.Demo != null) Text += " · " + L("demo.title");
            LayoutHeader(); LayoutComposer(); LayoutSidebar();
            People.ApplyStyle(Palette.From(th), Font);
            FirstRunPanel.BackColor = RenderStyle.C(th.Window);
            FirstRunTitle.BackColor = FirstRunPanel.BackColor; FirstRunTitle.ForeColor = RenderStyle.C(th.Text); FirstRunTitle.Font = Fonts.Create(s.FontFamily, Math.Max(11f, s.FontSize + 1), FontStyle.Bold);
            FirstRunNote.BackColor = FirstRunPanel.BackColor; FirstRunNote.ForeColor = RenderStyle.C(th.SubText); FirstRunNote.Font = Font;
            foreach (var r in new[] { FirstRunKo, FirstRunEn, FirstRunJa }) { r.Pal = Palette.From(th); r.Font = Font; }
            FirstRunStart.Font = Font; FirstRunStart.ForeColor = RenderStyle.C(th.Text); FirstRunStart.FlatAppearance.BorderSize = 1; FirstRunStart.FlatAppearance.BorderColor = RenderStyle.C(th.Border);
            UpdateFirstRunTexts();
            LayoutFirstRun();
            NoticeAction.Font = _headerPillFont;
            NoticeAction.Pill = true; NoticeAction.PillText = NoticeAction.Text;
            NoticeAction.PillFill = RenderStyle.C(th.Panel); NoticeAction.PillBorder = RenderStyle.C(th.Border);
            NoticeAction.ForeColor = RenderStyle.C(th.MidText);
            NoticeClose.Size = new Size(HeaderPill.HeightFor(_headerPillFont), HeaderPill.HeightFor(_headerPillFont));
            BlendButtons(th);
            FitNotice();
            Invalidate(true);
        }

        public RenderStyle CurrentStyle { get { return _prefsStyle; } }

        /// <summary>
        /// Every flat button sits on its parent's colour (no slightly different face) and hovers/presses in theme colours
        /// instead of Windows' default ones (2026-11-20 user request). The send button keeps its own look.
        /// </summary>
        private void BlendButtons(Theme th)
        {
            var accent = RenderStyle.C(th.Accent);
            foreach (var b in AllControls(this).OfType<ButtonBase>())
            {
                if (b == SendButton || b.Parent == null) continue;
                var back = b.Parent.BackColor;
                b.BackColor = back;
                b.FlatAppearance.MouseOverBackColor = Palette.Mix(back, accent, 0.12);
                b.FlatAppearance.MouseDownBackColor = Palette.Mix(back, accent, 0.25);
                b.FlatAppearance.CheckedBackColor = Palette.Mix(back, accent, 0.25);
            }
            NoticeAction.FlatAppearance.BorderSize = 0;
            NoticeAction.ForeColor = RenderStyle.C(th.MidText);
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in AllControls(c)) yield return d; }
        }

        public static float StatusLinePoints(float bodyPoints) { return Math.Max(9f, Math.Min(10f, bodyPoints - 1)); }

        /// <summary>Name and shortcut of every header/sidebar button (30-SPEC 3장: e.g. "검색 (Ctrl+F)").</summary>
        private void SetTips()
        {
            Tips.Set(RoomsButton, L("tip.header.rooms"));
            Tips.Set(SearchButton, L("tip.header.search"));
            Tips.Set(ReconnectButton, L("tip.header.reconnect"));
            Tips.Set(SettingsButton, L("tip.header.settings"));
            Tips.Set(MoreButton, L("tip.header.more"));
            Tips.Set(Faces, L("tip.header.participants"));
            Tips.Set(CollapseButton, L("tip.sidebar.collapse"));
            Tips.Set(ExpandButton, L("tip.sidebar.expand"));
            Tips.Set(GlobalSearchButton, L("tip.sidebar.globalSearch"));
            Tips.Set(NoticeClose, L("tip.notice.close"));
            Tips.Set(SearchPrev, L("tip.search.prev"));
            Tips.Set(SearchNext, L("tip.search.next"));
            Tips.Set(SearchCloseButton, L("tip.search.close"));
            Tips.Set(SendButton, L("tip.send"));
            foreach (var b in new Control[] { RoomsButton, SearchButton, ReconnectButton, SettingsButton, MoreButton, CollapseButton, ExpandButton, NoticeClose, SearchPrev, SearchNext, SearchCloseButton })
                b.AccessibleName = Tips.Get(b);
            Tips.Set(RoomSub, RoomSub.Text ?? "");
        }

        // ---------------------------------------------------------------- 90-SPEC 1.5 chat window size

        private bool _applyingSize;

        /// <summary>Applies the chosen size step (logical pixels × display scale). At start a dragged size is restored.</summary>
        public void ApplyWindowSize(bool startup)
        {
            var step = _prefs.Settings.Get("screen.windowSize");
            int dpi = DeviceDpiSafe;
            var px = WindowSizes.Pixels(step, dpi);
            if (px == null)
            {
                if (!startup) return;   // "직접 조절" is only shown, never applied
                var saved = _prefs.LoadWindowSize("main");
                px = saved == null ? WindowSizes.Pixels("default", dpi) : new[] { WindowSizes.Scale(saved[0], dpi), WindowSizes.Scale(saved[1], dpi) };
            }
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (_prefs.Settings.Get("screen.layout") == "sidebar" && !_prefs.Settings.GetBool("screen.sidebarCollapsed"))
            {
                var minimum = SizeFromClientSize(DisplayScale.Size(720, 520, dpi));
                px[0] = Math.Max(px[0], minimum.Width); px[1] = Math.Max(px[1], minimum.Height);
            }
            _applyingSize = true;
            try { Size = new Size(px[0], px[1]); }
            finally { _applyingSize = false; }
        }

        public int DeviceDpiSafe { get { try { return DeviceDpi > 0 ? DeviceDpi : 96; } catch (Exception) { return 96; } } }

        /// <summary>The user dragged the window: the step shows "직접 조절" unless the size still equals the step.</summary>
        public void OnUserResized()
        {
            if (_applyingSize || WindowState != FormWindowState.Normal) return;
            var px = WindowSizes.Pixels(_prefs.Settings.Get("screen.windowSize"), DeviceDpiSafe);
            if (px != null && px[0] == Width && px[1] == Height) return;
            _prefs.Settings.Set("screen.windowSize", WindowSizes.Custom);
        }

        // ---------------------------------------------------------------- notification preview (90-SPEC 1.4)

        /// <summary>Shows the chosen alert once (flash, badge) without touching the real unread count.</summary>
        public Action<IntPtr, bool> FlashTaskbar = Taskbar.Flash;
        public Action<IntPtr, int> SetTaskbarBadge = Taskbar.SetBadge;
        private string _notificationPreview;
        private DateTime _notificationPreviewUntil;
        private readonly Timer _notificationPreviewTimer = new Timer { Interval = 2500 };
        private bool _notificationPreviewWired;
        public string PreviewNotification(string mode)
        {
            _notificationPreviewTimer.Stop();
            _notificationPreview = mode == "off" ? null : mode;
            _notificationPreviewUntil = _host.Clock.UtcNow.AddMilliseconds(2500);
            if (!_notificationPreviewWired)
            {
                _notificationPreviewWired = true;
                _notificationPreviewTimer.Tick += (s, e) => { _notificationPreviewTimer.Stop(); _notificationPreview = null; if (!IsDisposed) UpdateBadges(); };
                Disposed += (s, e) => _notificationPreviewTimer.Dispose();
            }
            UpdateBadges();
            if (mode == "off") return L("settings.notifyPreviewOff");
            _notificationPreviewTimer.Start();
            return L(mode == "flashBadge" ? "settings.notifyPreviewBadge" : "settings.notifyPreviewFlash");
        }

        // ---------------------------------------------------------------- 90-SPEC 1.2 window opacity

        private readonly Timer _hoverTimer = new Timer { Interval = 150 };
        private int? _opacityPreview;
        private bool _hoverWired;
        public double OpacitySetting { get { return _prefs.Settings.GetInt("screen.opacity") / 100.0; } }
        public Func<bool> MouseOverWindow;

        /// <summary>Whole window (text included) at the chosen opacity; clicks and keyboard input keep working (layered window, not click-through).</summary>
        public void ApplyOpacity(int? preview = null)
        {
            _opacityPreview = preview;
            double target = (preview ?? _prefs.Settings.GetInt("screen.opacity")) / 100.0;
            target = Math.Max(0.40, Math.Min(1.0, target));
            bool hover = !preview.HasValue && _prefs.Settings.GetBool("screen.opacityHover") && MouseOverWindow != null && MouseOverWindow();
            var v = hover ? 1.0 : target;
            if (Math.Abs(Opacity - v) > 0.001) Opacity = v;
            if (!_hoverWired) { _hoverWired = true; _hoverTimer.Tick += (s, e) => ApplyOpacity(_opacityPreview); }
            _hoverTimer.Enabled = _prefs.Settings.GetBool("screen.opacityHover") && target < 1.0;
        }

        // ---------------------------------------------------------------- 90-SPEC 1.1 window border

        public int BorderThickness { get; private set; }
        /// <summary>True when the operating system draws the border (Windows 11 attribute call succeeded).</summary>
        public bool OsBorder { get; private set; }
        public IDwm Dwm = new RealDwm();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBorder();
        }
        /// <summary>Screen checks: treat the window as maximised without maximising it.</summary>
        public bool? MaximizedOverride;
        public Color BorderColor { get; private set; }

        /// <summary>
        /// The border is the form's own background showing through a padding of the chosen thickness, so it never covers
        /// the chat, scroll bar or resize grip. Thickness is in screen pixels. No border while maximised.
        /// </summary>
        public void ApplyBorder()
        {
            if (_prefsStyle == null) return;
            var th = _prefsStyle.Theme;
            string level = _prefs.Settings.Get("screen.border");
            int t = 0;
            NyangTalk.Domain.People.Rgb c = th.Border;
            switch (level)
            {
                case "light": t = 1; c = th.Border; break;
                case "normal": t = 1; c = NyangTalk.Domain.People.ColorMath.Mix(th.Border, NyangTalk.Domain.People.ColorMath.Luminance(th.Window) > 0.5 ? new NyangTalk.Domain.People.Rgb(0, 0, 0) : new NyangTalk.Domain.People.Rgb(255, 255, 255), 0.35); break;
                case "strong": t = 2; c = th.Accent; break;
            }
            if (MaximizedOverride ?? WindowState == FormWindowState.Maximized) t = 0;
            if (!_active) c = NyangTalk.Domain.People.ColorMath.Mix(c, th.Window, 0.5);   // inactive: one step lighter
            // 90-SPEC 1.3: on Windows 11 the window manager colours the title bar and the outer line; then the app draws none
            OsBorder = IsHandleCreated && TitleBar.Apply(Dwm, Handle, th, t > 0 ? (NyangTalk.Domain.People.Rgb?)c : null);
            if (OsBorder) t = 0;
            BorderThickness = t;
            BorderColor = RenderStyle.C(c);
            BackColor = t > 0 ? BorderColor : RenderStyle.C(th.Window);
            if (Padding.All != t || Padding.Left != t) Padding = new Padding(t);
        }

        private bool _active = true;
        public void SetActiveForBorder(bool active) { _active = active; ApplyBorder(); }

        /// <summary>A disabled send button must look disabled.</summary>
        private void PaintSendButton()
        {
            if (_prefsStyle == null) return;
            var th = _prefsStyle.Theme;
            float opacity = SendButton.Enabled ? 1f : HeaderPill.DisabledOpacity;
            SendButton.BackColor = HeaderPill.Composite(ComposerArea.BackColor, RenderStyle.C(th.Accent), opacity);
            SendButton.ForeColor = HeaderPill.Composite(ComposerArea.BackColor, RenderStyle.C(th.AccentText), opacity);
            SendButton.IconColor = SendButton.ForeColor;
        }

        // ---------------------------------------------------------------- refresh

        public void Tick()
        {
            if (_host.Demo != null) DemoStep();
            if (DateTime.UtcNow.Minute == 0 && DateTime.UtcNow.Second < 1) RunRetention();
            try { _chat.Poll(); }
            catch (IOException ex) { _host.AddLog("poll: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { _host.AddLog("poll: " + ex.Message); }
            UpdatePortraits();
            if (_dirty) RefreshChat();
            UpdateHeader();
            UpdateComposerState();
            UpdateBadges();
            UpdateAutomationPanel();
            if (TickHook != null) { try { TickHook(); } catch (Exception ex) { _host.AddLog("monitor: " + ex.Message); } }
        }

        private int _demoTick;
        private void DemoStep()
        {
            var now = _host.Clock.UtcNow;
            _host.Demo.WriteStatus(now);
            _host.Demo.WriteParticipants();
            _host.Demo.ProcessOutbox(now);
            if (++_demoTick % 6 == 0) _host.Demo.RandomChatter(now);
        }

        public void RefreshChat()
        {
            _dirty = false;
            _items = _chat.BuildTimeline(TimeZoneInfo.Local);
            var outgoing = _chat.IsArchivedView ? new List<OutgoingMessage>() : _chat.Outbox.Pending.ToList();
            Chat.SetContent(_items, outgoing, _chat.CanShowMore, _chat.Window.CanCollapse);
            if (SearchBar.Visible && SearchBox.Text.Length > 0) RunSearch(keepPosition: true);
        }

        public void UpdateHeader()
        {
            string title, sub;
            var st = _chat.Status;
            if (_chat.IsArchivedView)
            {
                title = _chat.ArchivedName ?? string.Format(L("room.unknownName"), LobbyInfo.IdSuffix(_chat.ArchivedLobbyId));
                sub = L("room.archivedBadge");
                ShowNotice("archived:" + _chat.ArchivedLobbyId, L("room.archivedBadge"));
            }
            else if (st != null && st.Lobby != null)
            {
                title = string.IsNullOrEmpty(st.Lobby.Name) ? string.Format(L("room.unknownName"), LobbyInfo.IdSuffix(st.Lobby.LobbyId)) : st.Lobby.Name;
                var parts = new List<string> { LinkText() };
                if (st.Lobby.DisplayCode != null) parts.Add(string.Format(L("room.code"), st.Lobby.DisplayCode));
                sub = string.Join(" · ", parts);
            }
            else { title = L("room.noLobby"); sub = LinkText(); }
            if (_host.Demo != null) sub = L("demo.notice");
            RoomTitle.Text = title; RoomSub.Text = sub;
            Tips.Set(RoomSub, sub);   // the full sentence when the line is cut (30-SPEC 3장)
            CurrentRoomButton.Text = (st != null && st.Lobby != null ? (st.Lobby.Name ?? LobbyInfo.IdSuffix(st.Lobby.LobbyId)) : L("room.noLobby")) + "\n" + L("room.currentSub")
                + (_chat.Unread.Unread > 0 ? "  (" + _chat.Unread.Unread + ")" : "");
            var people = _chat.PeopleList();
            Faces.SetPeople(people, st != null && st.Lobby != null && st.Lobby.MemberCount > 0 ? st.Lobby.MemberCount : people.Count);
            if (ParticipantsOpen) RefreshParticipants();
            var me = people.FirstOrDefault(p => p.IsMe);
            MyName.Text = me != null ? DisplayName.Format(me.Name, _prefs.Aliases.Get(me.SteamId), _prefs.NameMode) : "—";
            LinkDot.ForeColor = _chat.Link == LinkState.Connected ? Color.FromArgb(0x1F, 0x9D, 0x55) : (_chat.Link == LinkState.Waiting || _chat.Link == LinkState.Disconnected ? Color.FromArgb(0xC9, 0x8A, 0x00) : Color.Gray);
            LinkDot.AccessibleName = LinkText();
            if (st != null && !string.IsNullOrEmpty(st.Error)) ShowNotice("component-error:" + st.Error, string.Format(L("link.componentError"), st.Error));
            var broken = _chat.Areas.Broken.ToList();
            if (broken.Contains(FeatureArea.ChatSend)) ShowNotice("area:send", L("link.sendBroken"));
            else if (broken.Count > 0) ShowNotice("area:" + string.Join(",", broken), string.Format(L("link.areaBroken"), string.Join(", ", broken.Select(a => L("area." + a)))));
            LayoutHeader();
        }

        public string LinkText()
        {
            switch (_chat.Link)
            {
                case LinkState.Connected: return L("link.connected");
                case LinkState.Disconnected: return L("link.disconnected");
                case LinkState.NeedsComponentUpdate: return L("link.needsComponentUpdate");
                case LinkState.NeedsReinstall: return L("link.needsReinstall");
                case LinkState.NotInstalled: return L("link.notInstalled");
                default: return L("link.waiting");
            }
        }

        /// <summary>A plain notice (the lowest kind on the one line); a closed key is not shown again.</summary>
        public void ShowNotice(string key, string text)
        {
            if (Notices.IsDismissed(key)) return;
            Notices.Set(new NoticeEntry { Key = key, Kind = NoticeKind.Info, Text = text });
        }

        private void RenderNotice()
        {
            var cur = Notices.Current;
            if (cur == null) { NoticeBand.Visible = false; return; }
            NoticeLabel.Text = (cur.Kind == NoticeKind.Health ? "⚠ " : "") + cur.Text;
            NoticeAction.Visible = cur.ActionText != null;
            NoticeAction.Text = cur.ActionText ?? "";
            NoticeAction.PillText = NoticeAction.Text;
            NoticeAction.AccessibleName = cur.ActionText;
            NoticeBand.Visible = true;
            FitNotice();
        }

        /// <summary>
        /// The sentence wraps; the action button keeps its whole label. On a narrow window the button moves under the
        /// sentence instead of being cut (95-SPEC 2.6).
        /// </summary>
        private void FitNotice()
        {
            int inner = NoticeBand.ClientSize.Width - NoticeBand.Padding.Horizontal;
            int closeW = NoticeClose.Width;
            int btnW = NoticeAction.Visible ? TextRenderer.MeasureText(NoticeAction.Text, NoticeAction.Font).Width + 20 : 0;
            int btnH = NoticeAction.Visible ? HeaderPill.HeightFor(NoticeAction.Font) : 0;
            bool below = NoticeAction.Visible && inner - closeW - btnW - 12 < 120;
            int textW = Math.Max(40, inner - closeW - 6 - (NoticeAction.Visible && !below ? btnW + 6 : 0));
            int textH = TextRenderer.MeasureText(NoticeLabel.Text ?? "", NoticeLabel.Font, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height;
            int rowH = Math.Max(Math.Max(textH, btnH), 20) + 10;
            int h = below ? textH + 10 + btnH + 8 : rowH;
            h = Math.Max(30, h);
            if (NoticeBand.Height != h) NoticeBand.Height = h;
            int left = NoticeBand.Padding.Left;
            NoticeLabel.SetBounds(left, 5, textW, below ? textH : rowH - 10);
            NoticeClose.SetBounds(NoticeBand.ClientSize.Width - NoticeBand.Padding.Right - closeW, (Math.Min(h, rowH) - NoticeClose.Height) / 2, closeW, NoticeClose.Height);
            if (NoticeAction.Visible)
            {
                if (below) NoticeAction.SetBounds(Math.Max(left, left + textW - btnW), textH + 12, Math.Min(btnW, inner - closeW - 6), btnH);
                else NoticeAction.SetBounds(NoticeClose.Left - 6 - btnW, (rowH - btnH) / 2, btnW, btnH);
            }
        }

        public void UpdateComposerState()
        {
            var oldHint = HintLabel.Text; var oldCounter = CounterLabel.Text;
            int len = Outbox.Length(Input.Text);
            int limit = _chat.CharLimit;
            CounterLabel.Text = string.Format(L("send.counter"), len, limit > 0 ? limit.ToString() : "?");
            var block = _chat.CheckSend(Input.Text.Length == 0 ? "x" : Input.Text);
            bool lockedInput = _chat.IsArchivedView || _chat.Areas.SendLocked;
            Input.ReadOnly = lockedInput;
            SendButton.Enabled = Input.Text.Trim().Length > 0 && (block == SendBlock.None || block == SendBlock.Duplicate);
            if (block == SendBlock.TooLong || block == SendBlock.Disconnected || block == SendBlock.Archived || block == SendBlock.NotAllowed)
                HintLabel.Text = L("send.blocked." + block);
            else HintLabel.Text = L("send.hint");
            if (oldHint != HintLabel.Text || oldCounter.Length != CounterLabel.Text.Length) LayoutComposer();
        }

        public void DoSend()
        {
            var text = Input.Text;
            if (text.Trim().Length == 0) return;
            var block = _chat.Send(text);
            if (block == SendBlock.None) { Input.Clear(); Chat.ScrollToBottom(); }
            else HintLabel.Text = L("send.blocked." + block);
            _dirty = true;
            RefreshChat();
        }

        private void UpdateBadges()
        {
            var mode = _prefs.Settings.Get("notify.mode");
            _chat.Unread.Mode = mode == "off" ? NotifyMode.Off : (mode == "flash" ? NotifyMode.Flash : NotifyMode.FlashAndBadge);
            if (!IsHandleCreated) return;
            bool previewing = _notificationPreview != null && _host.Clock.UtcNow < _notificationPreviewUntil;
            FlashTaskbar(Handle, previewing || _chat.Unread.Flashing);
            SetTaskbarBadge(Handle, previewing ? (_notificationPreview == "flashBadge" ? 3 : 0) : _chat.Unread.Unread);
        }

        private bool _showAutomation;

        private void UpdateAutomationPanel()
        {
            var text = AutomationStatusText();
            bool show = (_layout == LayoutKind.Sidebar || SidebarOverlay) && !string.IsNullOrEmpty(text);
            AutomationLabel.Text = text ?? "";
            if (_showAutomation != show || AutomationPanel.Visible != show)
            {
                _showAutomation = show;
                AutomationPanel.Visible = show;
                LayoutSidebar();
            }
        }

        // ---------------------------------------------------------------- portraits

        private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private void UpdatePortraits()
        {
            if (!_prefs.Settings.GetBool("char.show")) return;
            var now = _host.Clock.UtcNow;
            foreach (var row in _host.Exchange.ReadAppearance())
            {
                if (row.SteamId == null) continue;
                var key = new NyangTalk.Domain.Appearance.AppearanceKey(row.Skin, row.Hat);
                _host.Portraits.SetLook(row.SteamId, key, now);
                var have = _host.Portraits.For(row.SteamId);
                if (row.PortraitFile != null && File.Exists(row.PortraitFile) && (have == null || !have.Key.Equals(key) || have.File != row.PortraitFile)) { _host.Portraits.OnRendered(key, row.PortraitFile, now); _dirty = true; Chat.InvalidateLayout(); }
                if (row.PortraitFile != null && _prefs.Settings.Get("char.scope") == "face")
                {
                    var face = NyangTalk.Shared.PortraitFiles.FaceVariant(row.PortraitFile);
                    if (!_images.ContainsKey(face) && File.Exists(face)) Chat.Invalidate();
                }
            }
        }

        private Image PictureAt(string file)
        {
            if (file == null) return null;
            Image img;
            if (_images.TryGetValue(file, out img)) return img;
            try { using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) img = new Bitmap(Image.FromStream(fs)); }
            catch (IOException) { img = null; }
            catch (ArgumentException) { img = null; }
            if (img != null) _images[file] = img;
            return img;
        }

        private Image PortraitFor(string steamId)
        {
            if (steamId == null) return null;
            var e = _host.Portraits.For(steamId);
            if (e == null) return null;
            string file = e.File;
            if (_prefs.Settings.Get("char.scope") == "face")
            {
                var face = NyangTalk.Shared.PortraitFiles.FaceVariant(file);
                if (File.Exists(face)) file = face;
            }
            return PictureAt(file);
        }

        // ---------------------------------------------------------------- search

        public void OpenSearch()
        {
            SearchBar.Visible = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        public void CloseSearch()
        {
            SearchBar.Visible = false;
            Chat.ClearHighlight();
            Input.Focus();
        }

        private void SearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { SearchStep(!e.Shift); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CloseSearch(); e.SuppressKeyPress = true; }
        }

        public void RunSearch(bool keepPosition = false)
        {
            _search.Run(SearchBox.Text, _items, id => _prefs.Aliases.Get(id), _prefs.NameMode,
                _prefs.Settings.GetBool("chat.searchIgnoreCase"), _prefs.Settings.GetBool("chat.searchIgnoreWidth"));
            SearchCount.Text = _search.Label;
            if (!keepPosition && _search.CurrentItemIndex >= 0) Chat.ScrollToItemKey(_items[_search.CurrentItemIndex].Key);
            if (_search.Hits.Count == 0) Chat.ClearHighlight();
        }

        public void SearchStep(bool forward)
        {
            int idx = forward ? _search.Next() : _search.Previous();
            SearchCount.Text = _search.Label;
            if (idx >= 0) Chat.ScrollToItemKey(_items[idx].Key);
        }

        // ---------------------------------------------------------------- keys

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.F: OpenSearch(); return true;
                case Keys.Control | Keys.K: OpenGlobalSearch(); return true;
                case Keys.Control | Keys.B: ToggleSidebar(); return true;
                case Keys.Escape:
                    if (SidebarOverlay) { CloseSidebarOverlay(); return true; }
                    if (ParticipantsOpen) { CloseParticipants(); return true; }
                    if (SearchBar.Visible) { CloseSearch(); return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- actions

        private readonly HashSet<string> _askResend = new HashSet<string>();

        /// <summary>Same as clicking a button inside the chat list (checks call this directly).</summary>
        public void OnChatAction(string action, Row row)
        {
            switch (action)
            {
                case "more": _chat.ShowMore(); _dirty = true; RefreshChat(); break;
                case "collapse": _chat.CollapseHistory(); _dirty = true; RefreshChat(); Chat.ScrollToBottom(); break;
                case "retry":
                    if (row.Outgoing == null) break;
                    // 95-SPEC 2.3: the result is unknown, so it may already be in the room; ask under the message first
                    if (row.Outgoing.State == OutgoingState.Unconfirmed) { _askResend.Add(row.Outgoing.RequestId); RefreshChat(); break; }
                    SendAgain(row.Outgoing.RequestId);
                    break;
                case "resendConfirm": if (row.Outgoing != null) SendAgain(row.Outgoing.RequestId); break;
                case "resendCancel": if (row.Outgoing != null) { _askResend.Remove(row.Outgoing.RequestId); RefreshChat(); } break;
                case "delete": if (row.Outgoing != null) { _askResend.Remove(row.Outgoing.RequestId); _chat.DeleteFailed(row.Outgoing.RequestId); _dirty = true; RefreshChat(); } break;
            }
        }

        private void SendAgain(string requestId)
        {
            _askResend.Remove(requestId);
            var b = _chat.Retry(requestId); // the draft in Input is not touched
            if (b != SendBlock.None) HintLabel.Text = L("send.blocked." + b);
            _dirty = true; RefreshChat();
        }

        private ContextMenuStrip _menu;

        private void OnPersonMenu(Row row, Point at)
        {
            _menu = new ContextMenuStrip();
            _menu.Items.Add(L("menu.copy"), null, (s, e) => Chat.CopySelected());
            if (row.SenderId != null)
            {
                var id = row.SenderId;
                var name = row.Item is MessageItem ? ((MessageItem)row.Item).Message.SenderName : id;
                AddPersonItems(_menu, id, name);
            }
            _menu.Show(Chat, at);
        }

        public void AddPersonItems(ContextMenuStrip menu, string steamId, string originalName)
        {
            menu.Items.Add(L("menu.alias"), null, (s, e) => Dialogs.EditAlias(this, _prefs, steamId, originalName));
            menu.Items.Add(L("menu.color"), null, (s, e) => Dialogs.PickColor(this, _prefs, steamId));
        }

        /// <summary>The people pill toggles the participants side panel.</summary>
        public void ShowParticipants()
        {
            if (ParticipantsOpen) CloseParticipants(); else OpenParticipants();
        }

        /// <summary>30-SPEC 3장: the saved-rooms icon opens a small popup with the current lobby and the saved rooms.</summary>
        public ContextMenuStrip RoomsMenu()
        {
            if (_menu != null) _menu.Dispose();
            var pal = Palette.From(_prefsStyle.Theme);
            _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(pal), BackColor = pal.Panel, ForeColor = pal.Text, Font = Font, ShowImageMargin = false, ShowCheckMargin = true };
            _menu.Items.Add(new ToolStripLabel(L("room.current")) { ForeColor = pal.Sub });
            var st = _chat.Status;
            var cur = new ToolStripMenuItem(st != null && st.Lobby != null ? (st.Lobby.Name ?? LobbyInfo.IdSuffix(st.Lobby.LobbyId)) : L("room.noLobby")) { Checked = !_chat.IsArchivedView };
            cur.Click += (s, e) => { if (_chat.IsArchivedView) _chat.CloseArchived(); };
            _menu.Items.Add(cur);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripLabel(L("room.archived")) { ForeColor = pal.Sub });
            var rooms = ArchivedRooms();
            if (rooms.Count == 0) _menu.Items.Add(new ToolStripLabel(L("room.noArchived")) { ForeColor = pal.Sub });
            foreach (var r in rooms.Take(20))
            {
                var room = r;
                var item = new ToolStripMenuItem(RoomLabel(room)) { Checked = _chat.IsArchivedView && _chat.ArchivedLobbyId == room.LobbyId };
                item.Click += (s, e) => _chat.OpenArchived(room.LobbyId, room.Name);
                _menu.Items.Add(item);
            }
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(L("search.global") + "  (Ctrl+K)", null, (s, e) => OpenGlobalSearch());
            _menu.Items.Add(L("menu.export"), null, (s, e) => Export());
            var auto = AutomationStatusText();
            if (!string.IsNullOrEmpty(auto)) _menu.Items.Add(auto, null, (s, e) => OpenSettings("auto"));
            return _menu;
        }

        private void ShowRoomsMenu(Control anchor) { RoomsMenu().Show(anchor, new Point(0, anchor.Height)); }

        /// <summary>"더 보기(…)" — only in a window too narrow for every icon (mini); holds the hidden icons' actions.</summary>
        public ContextMenuStrip MoreMenu()
        {
            if (_menu != null) _menu.Dispose();
            var pal = Palette.From(_prefsStyle.Theme);
            _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(pal), BackColor = pal.Panel, ForeColor = pal.Text, Font = Font, ShowImageMargin = false };
            if (Overflow.Contains(RoomsButton)) _menu.Items.Add(L("tip.header.rooms"), null, (s, e) => BeginInvoke((Action)(() => ShowRoomsMenu(MoreButton))));
            if (Overflow.Contains(SearchButton)) _menu.Items.Add(L("tip.header.search"), null, (s, e) => OpenSearch());
            if (Overflow.Contains(ReconnectButton)) _menu.Items.Add(L("tip.header.reconnect"), null, (s, e) => Reconnect());
            if (Overflow.Contains(Faces)) _menu.Items.Add(L("tip.header.participants"), null, (s, e) => ShowParticipants());
            return _menu;
        }

        private void ShowMoreMenu() { MoreMenu().Show(MoreButton, new Point(0, MoreButton.Height)); }

        public List<RoomSummary> ArchivedRooms()
        {
            return _chat.ListArchivedRooms();
        }

        public string RoomLabel(RoomSummary r)
        {
            var name = string.IsNullOrEmpty(r.Name) ? string.Format(L("room.unknownName"), LobbyInfo.IdSuffix(r.LobbyId)) : r.Name;
            if (r.IsGrouped) name += " (" + string.Format(L("room.groupedCount"), r.MemberLobbyIds.Count) + ")";
            return name + " · " + r.LastUtc.ToLocalTime().ToString("MM-dd HH:mm") + " · " + r.MessageCount;
        }

        private List<RoomSummary> _archive = new List<RoomSummary>();

        public void RefreshArchive()
        {
            _archive = ArchivedRooms();
            ArchiveList.BeginUpdate();
            ArchiveList.Items.Clear();
            foreach (var r in _archive) ArchiveList.Items.Add(RoomLabel(r));
            ArchiveList.EndUpdate();
        }

        private void OpenSelectedArchive()
        {
            int i = ArchiveList.SelectedIndex;
            if (i < 0 || i >= _archive.Count) return;
            _chat.OpenArchived(_archive[i].LobbyId, _archive[i].Name);
            _dirty = true; RefreshChat(); UpdateHeader();
        }

        private void DeleteSelectedArchive()
        {
            int i = ArchiveList.SelectedIndex;
            if (i < 0 || i >= _archive.Count) return;
            if (MessageBox.Show(this, L("archive.deleteConfirm"), "NyangTalk", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            if (_chat.ArchivedLobbyId == _archive[i].LobbyId) _chat.CloseArchived();
            _host.History.DeleteRoom(_archive[i].LobbyId);
            RefreshArchive();
        }

        private void ArchiveMenu(Point at)
        {
            int i = ArchiveList.IndexFromPoint(at);
            if (i < 0) return;
            ArchiveList.SelectedIndex = i;
            var room = _archive[i];
            var pal = Palette.From(_prefsStyle.Theme);
            _menu = new ContextMenuStrip { Renderer = new ThemedMenuRenderer(pal), BackColor = pal.Panel, ForeColor = pal.Text, Font = Font, ShowImageMargin = false };
            _menu.Items.Add(L("menu.rooms"), null, (s, e) => OpenSelectedArchive());
            if (_host.History.ListRooms().Count(r => r.LobbyId != _chat.CurrentLobbyId) >= 2)
                _menu.Items.Add(L("room.groupWith"), null, (s, e) => Dialogs.GroupRooms(this, _host, room.LobbyId, () => { RefreshArchive(); _dirty = true; RefreshChat(); UpdateHeader(); }));
            if (room.IsGrouped || _chat.RoomGroups.GroupOf(room.LobbyId) != null)
                _menu.Items.Add(L("room.ungroup"), null, (s, e) => { _chat.UngroupRooms(room.LobbyId); RefreshArchive(); _dirty = true; RefreshChat(); UpdateHeader(); });
            _menu.Items.Add(L("send.delete"), null, (s, e) => DeleteSelectedArchive());
            _menu.Show(ArchiveList, at);
        }

        public void OpenGlobalSearch()
        {
            Dialogs.GlobalSearch(this, _host, hit =>
            {
                if (hit.LobbyId == _chat.CurrentLobbyId) { if (_chat.IsArchivedView) _chat.CloseArchived(); }
                else _chat.OpenArchived(hit.LobbyId, hit.RoomName);
                _dirty = true; RefreshChat();
                var key = "msg:" + hit.Message.Id;
                while (!_items.Any(it => it.Key == key) && _chat.CanShowMore) { _chat.ShowMore(); RefreshChat(); }
                Chat.ScrollToItemKey(key);
                UpdateHeader();
            });
        }

        public void Export()
        {
            // 95-SPEC 0장: the warning is in the save window's title and on the notice line afterwards, not in a pop-up
            using (var dlg = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "nyangtalk-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt", Title = L("export.dialogTitle") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, _chat.Export(TimeZoneInfo.Local), new UTF8Encoding(false));
                    ShowNotice("export:" + DateTime.UtcNow.Ticks, string.Format(L("export.saved"), Path.GetFileName(dlg.FileName)) + " " + L("export.warning"));
                }
                catch (IOException ex) { ShowNotice("export:" + DateTime.UtcNow.Ticks, string.Format(L("export.failed"), ex.Message)); }
                catch (UnauthorizedAccessException ex) { ShowNotice("export:" + DateTime.UtcNow.Ticks, string.Format(L("export.failed"), ex.Message)); }
            }
        }

        /// <summary>Unsent draft survives restarts (20-SPEC 4장 7번, 01-OVERVIEW 3장 7번).</summary>
        public void SaveDraft()
        {
            _host.Docs.Save("draft", new Dictionary<string, object> { { "text", Input.Text } });
        }

        public void RestoreDraft()
        {
            string broken;
            var d = _host.Docs.Load("draft", out broken);
            object t;
            if (d != null && d.TryGetValue("text", out t) && t is string && Input.Text.Length == 0) Input.Text = (string)t;
        }

        public Action<string> SettingsOpener;

        public void OpenSettings(string category = null)
        {
            if (SettingsOpener != null) SettingsOpener(category);
        }

        private void DeletePortraits(IEnumerable<string> files)
        {
            foreach (var f in files.SelectMany(file => new[] { file, NyangTalk.Shared.PortraitFiles.FaceVariant(file) }).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Image img;
                if (_images.TryGetValue(f, out img)) { img.Dispose(); _images.Remove(f); }
                try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Records older than the retention period are removed at most once a day (90-SPEC 2장).</summary>
        public void RunRetention()
        {
            string broken;
            var d = _host.Docs.Load("retention", out broken);
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            object last;
            if (d != null && d.TryGetValue("last", out last) && (last as string) == today) return;
            int days = _prefs.Settings.GetInt("chat.retentionDays");
            try
            {
                _host.Exchange.WriteRetention(days);
                _host.History.Cleanup(DateTime.UtcNow, days);
                // 40-SPEC 1.2: exchange files follow the same period; trimmed here only while the game is not writing
                var st = _chat.Status;
                bool gameActive = st != null && DateTime.UtcNow - st.WrittenUtc <= TimeSpan.FromSeconds(6);
                _host.Exchange.TrimIdle(DateTime.UtcNow, days, gameActive);
            }
            catch (IOException ex) { _host.AddLog("retention: " + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { _host.AddLog("retention: " + ex.Message); return; }
            _host.Docs.Save("retention", new Dictionary<string, object> { { "last", today } });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            DeletePortraits(_host.Portraits.Clear());
            base.OnFormClosed(e);
        }
    }
}
