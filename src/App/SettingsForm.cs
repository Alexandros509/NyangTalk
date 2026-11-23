using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App.Controls;
using NyangTalk.App.Platform;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App
{
    /// <summary>
    /// Settings window (90-SPEC 1장, 1.4). Title and language on top, category buttons on the left, the chosen category on
    /// the right in meaning groups with sub-headings. Every change applies and saves at once. All colours come from the
    /// current theme (light or dark); the drawn controls in <see cref="Themed"/> keep Windows' default grey out.
    /// </summary>
    public sealed class SettingsForm : Form, IMessageFilter
    {
        public static readonly string[] Categories = { "screen", "chat", "people", "char", "auto", "link", "about" };

        /// <summary>Settings shown elsewhere: language in the top row, the game folder in the connection status.</summary>
        public static readonly string[] NotRows = { "ui.language", "link.gameDir" };

        /// <summary>Meaning groups: { category, heading key, setting keys… }. Every setting appears in exactly one group.</summary>
        public static readonly string[][] Groups =
        {
            new[] { "screen", "group.window", "screen.windowSize", "screen.layout", "screen.sidebarWidth", "screen.sidebarCollapsed", "screen.topmost", "screen.border", "screen.opacity", "screen.opacityHover" },
            new[] { "screen", "group.look", "screen.preset", "screen.theme", "screen.dark", "screen.chatShape", "screen.headerButtons", "screen.tailBubble" },
            new[] { "screen", "group.text", "screen.font", "screen.fontSize" },
            new[] { "chat", "group.emote", "chat.emoteDisplay", "chat.emoteMode", "chat.emoteWindow" },
            new[] { "chat", "group.messages", "chat.hideMeows", "chat.clock" },
            new[] { "chat", "group.notify", "notify.mode" },
            new[] { "chat", "group.records", "chat.retentionDays", "chat.searchIgnoreCase", "chat.searchIgnoreWidth" },
            new[] { "people", "group.names", "people.nameMode" },
            new[] { "people", "group.colors", "people.colorMode", "people.colorPlacement" },
            new[] { "char", "group.charShow", "char.show", "char.size", "char.compact", "char.shape", "char.scope" },
            new[] { "char", "group.charMine", "char.continuation", "char.mine", "char.minePosition" },
            new[] { "auto", "group.myChest", "auto.chest", "auto.chestTypes", "auto.chestDelay", "auto.chestMaxPerHour" },
            new[] { "auto", "group.gifts", "auto.gifts" },
            new[] { "about", "group.updates", "update.mode", "update.channel" },
            new[] { "link", "group.autoStart", "link.startWithGame" },
        };

        private readonly PreferencesService _prefs;
        private readonly Panel _top = new Panel();
        private readonly Label _title = new Label();
        private readonly ThemedChoice _lang = new ThemedChoice();
        private readonly Panel _banner = new Panel();
        private readonly Label _bannerText = new Label();
        private readonly Button _reload = new Button();
        private readonly CategoryList _cats = new CategoryList();
        private readonly ScrollArea _area = new ScrollArea();
        private readonly Dictionary<string, int> _scroll = new Dictionary<string, int>();
        public readonly HoverTips Tips = new HoverTips();
        private SettingsPage _page;
        private Palette _pal;
        private bool _rebuildQueued;

        public Func<string, string, bool> BeforeChange = (k, v) => true;   // e.g. automation consent
        /// <summary>Opacity while the slider moves (90-SPEC 1.2), before it is saved.</summary>
        public static Action<int> OpacityPreview;
        /// <summary>Whole pages built elsewhere (connection, automation, updates); false when it does not build that category.</summary>
        public Func<string, SettingsPage, bool> PageBuilder = (c, p) => false;
        /// <summary>One status line under a setting (automation: "꺼짐", "상자 준비를 기다리는 중", …); null for none.</summary>
        public Func<string, string> StatusLine = k => null;
        public Func<Size> MainWindowSize = () => Size.Empty;
        public Func<int> MainWindowDpi = () => 96;
        public Func<bool> SidebarLayout;
        public Func<string, string> NotifyPreview;           // mode → what was shown
        public Action EditMyAlias, OpenAliasList;
        /// <summary>{game name, my alias or null}, or null while the game has not said who I am.</summary>
        public Func<string[]> MyAlias;
        public IDwm Dwm = new RealDwm();
        public string Category { get; private set; }

        public SettingsForm(PreferencesService prefs)
        {
            _prefs = prefs;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent;
            int dpi = NyangTalk.App.Platform.DisplayScale.Dpi(this);
            MinimumSize = NyangTalk.App.Platform.DisplayScale.Size(560, 420, dpi);
            var saved = prefs.LoadWindowSize("settings");
            Size = saved != null ? NyangTalk.App.Platform.DisplayScale.Size(Math.Max(560, saved[0]), Math.Max(420, saved[1]), dpi) : NyangTalk.App.Platform.DisplayScale.Size(720, 680, dpi);
            Icon = AppIcon.Load();
            SidebarLayout = () => prefs.Settings.Get("screen.layout") != "single";

            _top.Dock = DockStyle.Top; _top.Height = 52; _top.Padding = new Padding(18, 8, 14, 8);
            _title.AutoSize = false; _title.TextAlign = ContentAlignment.MiddleLeft;
            _lang.Width = 150;
            _lang.Picked += i => _prefs.Settings.Set("ui.language", Strings.Languages[i]);
            _top.Controls.Add(_title); _top.Controls.Add(_lang);
            _top.Resize += (s, e) => LayoutTop();

            _banner.Dock = DockStyle.Top; _banner.Visible = false; _banner.Padding = new Padding(18, 6, 14, 6);
            _bannerText.AutoSize = false; _bannerText.Tag = "warn";
            _reload.AutoSize = true;
            _reload.Click += (s, e) => { if (_prefs.ReloadSettings()) HideBanner(); else ShowBanner(L("settings.reloadFailed"), true); };
            _banner.Controls.Add(_bannerText); _banner.Controls.Add(_reload);
            _banner.Resize += (s, e) => LayoutBanner();

            _cats.Dock = DockStyle.Left; _cats.Width = 172;
            _cats.SelectedChanged += i => ShowCategory(Categories[i]);
            _area.Dock = DockStyle.Fill;
            _area.Resize += (s, e) => Relayout();

            Controls.Add(_area); Controls.Add(_cats); Controls.Add(_banner); Controls.Add(_top);

            prefs.Settings.Changed += OnSettingChanged;
            prefs.SaveFailed += OnSaveFailed;
            FormClosed += (s, e) =>
            {
                prefs.Settings.Changed -= OnSettingChanged;
                prefs.SaveFailed -= OnSaveFailed;
                System.Windows.Forms.Application.RemoveMessageFilter(this);
                if (WindowState == FormWindowState.Normal) prefs.SaveWindowSize("settings", NyangTalk.App.Platform.DisplayScale.Logical(Width, NyangTalk.App.Platform.DisplayScale.Dpi(this)), NyangTalk.App.Platform.DisplayScale.Logical(Height, NyangTalk.App.Platform.DisplayScale.Dpi(this)));
                Tips.Dispose();
            };
            ApplyTexts();
            ApplyTheme();
            if (prefs.BrokenSettingsCopy != null) ShowBanner(string.Format(L("settings.broken"), System.IO.Path.GetFileName(prefs.BrokenSettingsCopy)), true);
            ShowCategory(Categories[0]);
        }

        public string L(string key) { return _prefs.L(key); }
        public PreferencesService Prefs { get { return _prefs; } }
        public Palette Pal { get { return _pal; } }
        public ScrollArea Area { get { return _area; } }
        public SettingsPage Page { get { return _page; } }
        public CategoryList CategoryButtons { get { return _cats; } }
        public ThemedChoice LanguagePicker { get { return _lang; } }
        public bool BannerVisible { get { return _banner.Visible; } }
        public string BannerText { get { return _bannerText.Text; } }

        public static string CategoryOf(string key)
        {
            foreach (var g in Groups) for (int i = 2; i < g.Length; i++) if (g[i] == key) return g[0];
            return key == "link.gameDir" ? "link" : "screen";
        }

        // ---------------------------------------------------------------- frame

        private void ApplyTexts()
        {
            Text = L("menu.settings");
            _title.Text = L("settings.title");
            _lang.Items = Strings.Languages.Select(x => Strings.ValueLabel("ui.language", x, x)).ToArray();
            _lang.SelectedIndex = Array.IndexOf(Strings.Languages, _prefs.Lang);
            _lang.AccessibleName = L("set.ui.language");
            Tips.Set(_lang, L("tip.ui.language"));
            _reload.Text = L("settings.reload");
            int sel = Category == null ? 0 : Array.IndexOf(Categories, Category);
            _cats.Items.Clear();
            foreach (var c in Categories) _cats.Items.Add(L("settings.cat." + c));
            _cats.AccessibleName = L("menu.settings");
            _cats.Invalidate();
        }

        private void LayoutTop()
        {
            _top.Height = Math.Max(NyangTalk.App.Platform.DisplayScale.Pixels(52, NyangTalk.App.Platform.DisplayScale.Dpi(this)),
                Math.Max(_title.Font.Height, _lang.Height) + _top.Padding.Vertical);
            _lang.Width = _lang.PreferredWidthFor(130, 180);
            _lang.Location = new Point(_top.ClientSize.Width - _top.Padding.Right - _lang.Width, (_top.Height - _lang.Height) / 2);
            _title.SetBounds(_top.Padding.Left, 0, Math.Max(40, _lang.Left - _top.Padding.Left - 8), _top.Height);
        }

        private void ShowBanner(string text, bool withReload)
        {
            _bannerText.Text = text;
            _reload.Visible = withReload;
            _banner.Visible = true;
            LayoutBanner();
        }

        private void HideBanner() { _banner.Visible = false; }

        private void OnSaveFailed(string key) { if (!IsDisposed) ShowBanner(L("settings.saveFailed"), false); }

        private void LayoutBanner()
        {
            int w = _banner.ClientSize.Width - _banner.Padding.Horizontal - (_reload.Visible ? _reload.Width + 10 : 0);
            int h = TextRenderer.MeasureText(_bannerText.Text ?? "", _bannerText.Font, new Size(Math.Max(40, w), int.MaxValue), TextFormatFlags.WordBreak).Height;
            _bannerText.SetBounds(_banner.Padding.Left, _banner.Padding.Top, Math.Max(40, w), h);
            _banner.Height = Math.Max(h, _reload.Visible ? _reload.Height : 0) + _banner.Padding.Vertical;
            _reload.Location = new Point(_banner.ClientSize.Width - _banner.Padding.Right - _reload.Width, _banner.Padding.Top);
        }

        /// <summary>Theme colours for the whole window, title bar included (90-SPEC 1.3, 1.4).</summary>
        public void ApplyTheme()
        {
            var style = RenderStyle.From(_prefs.Settings, _prefs.Colors, _prefs.NameMode);
            _pal = Palette.From(style.Theme);
            _pal.Back = _pal.Panel;
            Font = Fonts.Create(style.FontFamily, Math.Max(10f, style.FontSize));
            _title.Font = Fonts.Create(style.FontFamily, Math.Max(10f, style.FontSize) + 5, FontStyle.Bold);
            BackColor = _pal.Back; ForeColor = _pal.Text;
            _top.BackColor = _pal.Panel; _title.BackColor = _pal.Panel; _title.ForeColor = _pal.Text;
            _lang.Pal = _pal; _lang.BackColor = _pal.Panel;
            _banner.BackColor = _pal.Panel; _bannerText.BackColor = _pal.Panel; _bannerText.ForeColor = _pal.Warning;
            _pal.StyleButton(_reload);
            _cats.Pal = _pal;
            _area.Pal = _pal;
            if (_page != null) _page.Recolor(_pal);
            if (IsHandleCreated) TitleBar.Apply(Dwm, Handle, style.Theme, style.Theme.Border);
            LayoutTop();
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var th = RenderStyle.From(_prefs.Settings, _prefs.Colors, _prefs.NameMode).Theme;
            TitleBar.Apply(Dwm, Handle, th, th.Border);
            System.Windows.Forms.Application.AddMessageFilter(this);
        }

        // ---------------------------------------------------------------- categories

        public void ShowCategory(string cat)
        {
            int idx = Array.IndexOf(Categories, cat);
            if (idx < 0) { idx = 0; cat = Categories[0]; }
            if (_cats.SelectedIndex != idx) { _cats.SelectedIndex = idx; return; }   // comes back here through SelectedChanged
            if (Category != null && _page != null) _scroll[Category] = _area.Offset;
            Category = cat;
            Build();
            int y;
            _area.ScrollTo(_scroll.TryGetValue(cat, out y) ? y : 0);   // first visit from the top, revisits where they were
        }

        public void NextCategory(int step)
        {
            int i = (Array.IndexOf(Categories, Category) + step + Categories.Length) % Categories.Length;
            ShowCategory(Categories[i]);
        }

        private void Build()
        {
            _area.Content.SuspendLayout();
            var old = _page;
            _page = new SettingsPage(this, _area.Content);
            if (!PageBuilder(Category, _page)) BuildStandard(Category, _page);
            if (old != null) old.Dispose();
            _page.Recolor(_pal);
            _area.Content.ResumeLayout(false);
            UpdateEnabled();
            _page.RefreshNotes();
            Relayout();
        }

        /// <summary>Settings rows of the four plain categories with their previews next to the related items.</summary>
        private void BuildStandard(string cat, SettingsPage p)
        {
            switch (cat)
            {
                case "screen":
                    p.Group("screen", "group.window");
                    p.Group("screen", "group.look");
                    p.Preview("preview.look", 210, v => Previews.Conversation(this, v, false));
                    p.Group("screen", "group.text");
                    break;
                case "chat":
                    p.Group("chat", "group.emote");
                    p.Preview("preview.emote", 230, v => Previews.Emotes(this, v));
                    p.Group("chat", "group.messages");
                    p.Group("chat", "group.notify");
                    var result = p.Note("", false);
                    p.Buttons(p.Button(L("settings.notifyPreview"), () => { result.Text = NotifyPreview == null ? "" : NotifyPreview(_prefs.Settings.Get("notify.mode")); p.Relayout(); }, "tip.notifyPreview"));
                    p.Group("chat", "group.records");
                    break;
                case "people":
                    // 내 별명 first, with its current value, so it is found without knowing about right-click
                    p.Heading(L("group.myAlias"));
                    var me = MyAlias == null ? null : MyAlias();
                    p.Note(me == null ? L("people.noMe") : string.Format(L("people.myAliasNow"), string.IsNullOrEmpty(me[1]) ? L("people.myAliasNone") : me[1], me[0]), false);
                    var mine = p.Button(L("people.myAlias"), () => { if (EditMyAlias != null) { EditMyAlias(); RebuildNow(); } }, "tip.people.myAlias");
                    mine.Enabled = me != null && EditMyAlias != null;
                    p.Buttons(mine, p.Button(L("people.aliasList"), () => { if (OpenAliasList != null) OpenAliasList(); }, "tip.people.aliasList"));
                    p.Note(L("people.rightClickHint"), false);
                    p.Group("people", "group.names");
                    p.Group("people", "group.colors");
                    p.Preview("preview.people", 230, v => Previews.Conversation(this, v, true));
                    break;
                case "char":
                    p.Group("char", "group.charShow");
                    p.Group("char", "group.charMine");
                    p.Preview("preview.char", 230, v => Previews.Characters(this, v, false));
                    p.Preview("preview.charCompact", 130, v => Previews.Characters(this, v, true));
                    break;
                default:
                    foreach (var g in Groups.Where(x => x[0] == cat)) p.Group(cat, g[1]);
                    break;
            }
        }

        private void Relayout()
        {
            if (_page == null) return;
            _area.SetContentHeight(_page.Layout(_area.ContentWidth));
            _area.SetContentHeight(_page.Layout(_area.ContentWidth));   // twice: the scroll bar may have appeared
        }

        /// <summary>Keys that have a row on the page shown now.</summary>
        public IEnumerable<string> RowKeys { get { return _page == null ? Enumerable.Empty<string>() : _page.Rows.Keys; } }
        public bool IsEditorEnabled(string key) { var c = Editor(key); return c != null && c.Enabled; }
        public Control Editor(string key) { SettingsPage.Row r; return _page != null && _page.Rows.TryGetValue(key, out r) ? r.Editor : null; }

        // ---------------------------------------------------------------- changes

        private void OnSettingChanged(string key)
        {
            if (IsDisposed) return;
            if (key == "ui.language" || key == "screen.font" || key == "screen.fontSize")
            {
                // text and sizes change: rebuild after the current click has finished
                if (key == "ui.language") ApplyTexts();
                ApplyTheme();
                QueueRebuild();
                return;
            }
            if (key == "screen.theme" || key == "screen.dark") ApplyTheme();
            if (_page != null) _page.Sync();
            UpdateEnabled();
            if (_page != null) { _page.RefreshPreviews(); _page.RefreshNotes(); Relayout(); }
        }

        private void QueueRebuild()
        {
            if (_rebuildQueued) return;
            if (!IsHandleCreated) { RebuildNow(); return; }
            _rebuildQueued = true;
            BeginInvoke((Action)RebuildNow);
        }

        public void RebuildNow()
        {
            _rebuildQueued = false;
            if (IsDisposed) return;
            int y = _area.Offset;
            Build();
            _area.ScrollTo(y);
        }

        /// <summary>Items that mean nothing because of an earlier item are greyed, never hidden (90-SPEC 1.4).</summary>
        public void UpdateEnabled()
        {
            if (_page == null) return;
            var s = _prefs.Settings;
            _page.Enable("screen.tailBubble", s.Get("screen.preset") == "mobile" && s.Get("screen.chatShape") == "bubble");
            bool sidebar = SidebarLayout == null || SidebarLayout();
            _page.Enable("screen.sidebarWidth", sidebar);
            _page.Enable("screen.sidebarCollapsed", sidebar);
            _page.Enable("screen.opacityHover", s.GetInt("screen.opacity") < 100);
            _page.Enable("people.colorPlacement", s.Get("people.colorMode") != "none");
            bool cats = s.GetBool("char.show");
            foreach (var k in new[] { "char.size", "char.compact", "char.shape", "char.scope", "char.continuation", "char.mine" }) _page.Enable(k, cats);
            _page.Enable("char.minePosition", cats && s.Get("char.mine") == "show");
            bool auto = s.GetBool("auto.chest") || s.GetBool("auto.gifts");
            foreach (var k in new[] { "auto.chestTypes", "auto.chestDelay", "auto.chestMaxPerHour" }) _page.Enable(k, auto);
        }

        /// <summary>The line under the window size: real pixels now, and "직접 조절" when the user dragged it (90-SPEC 1.5).</summary>
        public string WindowSizeNote()
        {
            var sz = MainWindowSize();
            if (sz.IsEmpty) return "";
            int dpi = MainWindowDpi();
            var t = string.Format(L("settings.windowNow"), sz.Width, sz.Height, (int)Math.Round(dpi * 100 / 96.0));
            if (_prefs.Settings.Get("screen.windowSize") == WindowSizes.Custom) t += " · " + L("settings.windowCustom");
            return t;
        }

        // ---------------------------------------------------------------- keyboard and wheel

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Control | Keys.Tab: NextCategory(1); _cats.Focus(); return true;
                case Keys.Control | Keys.Shift | Keys.Tab: NextCategory(-1); _cats.Focus(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private const int WM_MOUSEWHEEL = 0x020A;

        /// <summary>The wheel scrolls the page wherever the pointer is over it (child controls would otherwise swallow it).</summary>
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL || IsDisposed || !Visible || Form.ActiveForm != this) return false;
            var screen = _area.RectangleToScreen(_area.ClientRectangle);
            if (!screen.Contains(Cursor.Position)) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            _area.ScrollBy(-delta / 120 * 48);
            return true;
        }
    }

    /// <summary>
    /// One category's content: headings, rows, notes, buttons and previews, laid out to the window width (labels wrap,
    /// no horizontal scrolling, label and control start lines aligned within the page).
    /// </summary>
    public sealed class SettingsPage : IDisposable
    {
        public sealed class Row
        {
            public string Key;
            public Label Label;           // null for check boxes (the box carries the text)
            public Control Editor;
            public List<ThemedCheck> Radios;
            public Label Value;           // slider value ("85%")
            public Label Note;            // always-visible explanation or status line
        }

        private readonly SettingsForm _form;
        private readonly Control _host;
        private readonly List<Func<int, int, int>> _blocks = new List<Func<int, int, int>>();
        private readonly List<Action> _previews = new List<Action>();
        private readonly List<Action> _notes = new List<Action>();
        public readonly Dictionary<string, Row> Rows = new Dictionary<string, Row>();
        private readonly List<Control> _all = new List<Control>();
        public const int Margin = 18, LabelMin = 150, LabelMax = 260, EditorMin = 150, EditorMax = 220;
        private int _width;

        public SettingsPage(SettingsForm form, Control host) { _form = form; _host = host; }

        private string L(string k) { return _form.L(k); }
        public HoverTips Tips { get { return _form.Tips; } }
        private Settings S { get { return _form.Prefs.Settings; } }

        private T Add<T>(T c) where T : Control
        {
            _host.Controls.Add(c);
            _all.Add(c);
            c.Enter += (s, e) => { var area = _form.Area; if (area != null) area.EnsureVisible(c); };
            return c;
        }

        private Label NewLabel(string text, string tag)
        {
            var l = Add(new Label { Text = text, AutoSize = false, Tag = tag, UseMnemonic = false });
            if (tag == "head") l.Font = new Font(_form.Font.FontFamily, _form.Font.Size + 2, FontStyle.Bold);
            if (tag == "sub" || tag == "warn") l.Font = new Font(_form.Font.FontFamily, Math.Max(9f, _form.Font.Size - 1));
            return l;
        }

        /// <summary>Height the label itself needs at a width (its own line breaking, not a separate measurement).</summary>
        private static int TextHeight(Label l, int w)
        {
            var empty = string.IsNullOrEmpty(l.Text);
            if (empty) l.Text = " ";
            int own = l.GetPreferredSize(new Size(Math.Max(10, w), 0)).Height - l.Padding.Vertical;
            // the label's own drawing breaks lines like a text box; take the larger so a line is never cut
            int box = TextRenderer.MeasureText(l.Text, l.Font, new Size(Math.Max(10, w - l.Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
            if (empty) l.Text = "";
            return Math.Max(own, box);
        }

        public void Heading(string text)
        {
            var l = NewLabel(text, "head");
            _blocks.Add((y, w) =>
            {
                int top = y == Margin ? 0 : 14;
                l.SetBounds(Margin, y + top, w, TextHeight(l, w) + 2);
                return top + l.Height + 6;
            });
        }

        /// <summary>A meaning group: sub-heading and its settings (definition in <see cref="SettingsForm.Groups"/>).</summary>
        public void Group(string cat, string heading)
        {
            var g = SettingsForm.Groups.First(x => x[0] == cat && x[1] == heading);
            Heading(L(heading));
            for (int i = 2; i < g.Length; i++) Setting(g[i]);
        }

        public Label Note(string text, bool warn)
        {
            var l = NewLabel(text, warn ? "warn" : "sub");
            _blocks.Add((y, w) =>
            {
                if (string.IsNullOrEmpty(l.Text)) { l.SetBounds(Margin, y, w, 0); return 0; }
                l.SetBounds(Margin, y, w, TextHeight(l, w) + 2);
                return l.Height + 6;
            });
            return l;
        }

        public void SetNoteWarn(Label l, bool warn) { l.Tag = warn ? "warn" : "sub"; l.ForeColor = warn ? _form.Pal.Warning : _form.Pal.Sub; }

        /// <summary>A band in the accent colour (e.g. "new version available").</summary>
        public Label Band(string text)
        {
            var l = NewLabel(text, "band");
            l.Padding = new Padding(8, 6, 8, 6);
            _blocks.Add((y, w) =>
            {
                if (string.IsNullOrEmpty(l.Text)) { l.SetBounds(Margin, y, w, 0); return 0; }
                l.SetBounds(Margin, y, w, TextHeight(l, w - 16) + 14);
                return l.Height + 6;
            });
            return l;
        }

        public Button Button(string text, Action click, string tipKey)
        {
            var b = Add(new IconButton(IconKind.None) { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, UseMnemonic = false, Padding = new Padding(6, 2, 6, 2) });
            if (click != null) b.Click += (s, e) => click();
            if (tipKey != null) _form.Tips.Set(b, L(tipKey));
            return b;
        }

        /// <summary>Buttons left to right, wrapping to the next line when the window is narrow.</summary>
        public void Buttons(params Button[] buttons)
        {
            _blocks.Add((y, w) =>
            {
                int x = 0, line = 0, h = 0;
                foreach (var b in buttons.Where(b => b.Visible))
                {
                    var sz = b.GetPreferredSize(Size.Empty);
                    int bw = Math.Min(w, sz.Width);
                    if (x > 0 && x + bw > w) { x = 0; line += h + 6; h = 0; }
                    b.SetBounds(Margin + x, y + line, bw, sz.Height);
                    x += bw + 8; h = Math.Max(h, sz.Height);
                }
                return line + h + 8;
            });
        }

        /// <summary>A control spanning the page width (previews, lists).</summary>
        public void Custom(Control c, int height) { Custom(c, w => height); }

        public void Custom(Control c, Func<int, int> heightForWidth)
        {
            Add(c);
            _blocks.Add((y, w) => { int h = heightForWidth(w); c.SetBounds(Margin, y, w, h); return h + 8; });
        }

        /// <summary>A preview right under the items it shows; refreshed whenever a setting changes.</summary>
        public void Preview(string titleKey, int height, Action<ChatView> fill)
        {
            var title = NewLabel(L(titleKey), "sub");
            var view = new ChatView { AccessibleName = L(titleKey), TabStop = false, ScrollBarVisible = false };
            _blocks.Add((y, w) => { title.SetBounds(Margin, y + 4, w, TextHeight(title, w) + 2); return title.Height + 6; });
            // as tall as the sample conversation at this width, so no message is cut (height is only the upper limit)
            Custom(view, w => { if (view.Width != w) view.Width = w; return Math.Max(60, Math.Min(Math.Max(height, 600), view.TotalHeight + 2)); });
            Action refresh = () => fill(view);
            _previews.Add(refresh);
            refresh();
        }

        public void RefreshPreviews() { foreach (var p in _previews) p(); }
        public void RefreshNotes() { foreach (var n in _notes) n(); }

        public Control Setting(string key)
        {
            var d = Settings.Def(key);
            var row = new Row { Key = key };
            string label = L("set." + key);
            string tip = L("tip." + key);
            Control editor;
            if (d.Choices != null && d.Choices.Length == 2 && d.Choices.Contains("true") && d.Choices.Contains("false"))
            {
                var cb = Add(new ThemedCheck { Text = label, Checked = S.GetBool(key) });
                cb.BeforeToggle = v => _form.BeforeChange(key, v ? "true" : "false");
                cb.CheckedChanged += (s, e) => { if (S.GetBool(key) != cb.Checked) S.Set(key, cb.Checked); };
                editor = cb;
            }
            else
            {
                row.Label = NewLabel(label, "label");
                _form.Tips.Set(row.Label, tip);
                if (d.Choices != null && d.Choices.Length == 2)
                {
                    // two values: radio buttons, never a two-value drop-down (90-SPEC 1.4)
                    var panel = Add(new Panel { TabStop = false });
                    row.Radios = new List<ThemedCheck>();
                    foreach (var v in d.Choices)
                    {
                        var value = v;
                        var rb = new ThemedCheck { Radio = true, Text = Strings.ValueLabel(key, v, _form.Prefs.Lang), Checked = S.Get(key) == v, AccessibleRole = AccessibleRole.RadioButton };
                        rb.BeforeToggle = on => _form.BeforeChange(key, value);
                        rb.CheckedChanged += (s, e) => { if (rb.Checked && S.Get(key) != value) S.Set(key, value); };
                        rb.Enter += (s, e) => { var area = _form.Area; if (area != null) area.EnsureVisible(rb); };
                        _form.Tips.Set(rb, tip);
                        panel.Controls.Add(rb);
                        row.Radios.Add(rb);
                    }
                    editor = panel;
                }
                else if (d.Choices != null)
                {
                    var choices = key == "screen.windowSize" ? WindowSizes.Steps : d.Choices;
                    var ch = Add(new ThemedChoice { Items = choices.Select(v => Strings.ValueLabel(key, v, _form.Prefs.Lang)).ToArray(), AccessibleName = label });
                    ch.Placeholder = Strings.ValueLabel(key, WindowSizes.Custom, _form.Prefs.Lang);
                    ch.SelectedIndex = Array.IndexOf(choices, S.Get(key));
                    ch.Picked += i => { var v = choices[i]; if (_form.BeforeChange(key, v)) S.Set(key, v); };
                    editor = ch;
                }
                else if (key == "screen.opacity")
                {
                    var sl = Add(new ThemedSlider { Minimum = d.Min.Value, Maximum = d.Max.Value, Step = d.Step, AccessibleName = label });
                    sl.Value = S.GetInt(key);
                    row.Value = NewLabel(sl.Value + "%", "label");
                    sl.Sliding += v => { row.Value.Text = v + "%"; var h = SettingsForm.OpacityPreview; if (h != null) h(v); };   // shown while dragging
                    sl.Committed += v => S.Set(key, v);                                                                       // saved on release
                    editor = sl;
                }
                else if (d.Min.HasValue)
                {
                    var st = Add(new ThemedStepper { Minimum = d.Min.Value, Maximum = d.Max.Value, Step = d.Step, AccessibleName = label });
                    st.Value = S.GetInt(key);
                    st.ValueChanged += v => S.Set(key, v);
                    editor = st;
                }
                else
                {
                    var tb = Add(new TextBox { Text = S.Get(key), AccessibleName = label, BorderStyle = BorderStyle.FixedSingle });
                    var saved = NewLabel("", "sub");
                    Action save = () => { S.Set(key, tb.Text.Trim()); saved.Text = L("settings.saved"); };
                    tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { save(); e.SuppressKeyPress = true; } };
                    tb.Leave += (s, e) => save();
                    row.Note = saved;
                    editor = tb;
                }
            }
            _form.Tips.Set(editor, tip);
            row.Editor = editor;
            if (row.Note == null)
            {
                Func<string> noteText = () =>
                {
                    if (key == "screen.windowSize") return _form.WindowSizeNote();
                    var status = _form.StatusLine(key);
                    if (status != null) return status;
                    // a greyed item says why it is off (90-SPEC 1.4: disabled, not hidden)
                    if (!row.Editor.Enabled && Strings.Table.ContainsKey("why." + key)) return L("why." + key);
                    return Strings.Table.ContainsKey("note." + key) ? L("note." + key) : null;
                };
                bool canHaveNote = key == "screen.windowSize" || _form.StatusLine(key) != null || Strings.Table.ContainsKey("why." + key) || Strings.Table.ContainsKey("note." + key);
                if (canHaveNote)
                {
                    row.Note = NewLabel(noteText() ?? "", "sub");
                    _notes.Add(() => row.Note.Text = noteText() ?? "");
                }
            }
            Rows[key] = row;
            _blocks.Add((y, w) => LayoutRow(row, y, w));
            return editor;
        }

        private int LabelWidth(int w) { return Math.Max(LabelMin, Math.Min(LabelMax, (int)(w * 0.4))); }

        private int LayoutRow(Row r, int y, int w)
        {
            int h;
            int indent = 0;
            if (r.Label == null)
            {
                var cb = (ThemedCheck)r.Editor;
                int cw = Math.Min(w, cb.PreferredWidthFor(w));
                cb.SetBounds(Margin, y, cw, cb.HeightFor(cw));
                h = cb.Height;
                indent = 24;
            }
            else
            {
                int lw = LabelWidth(w);
                int ex = lw + 16, avail = w - ex;
                bool stack = avail < EditorMin;
                if (stack) { lw = w; ex = 0; avail = w; }
                int lh = TextHeight(r.Label, lw);
                int valueWidth = r.Value == null ? 0 : Math.Max(52, r.Value.GetPreferredSize(Size.Empty).Width + 4);
                int ew, eh;
                if (r.Radios != null)
                {
                    int total = r.Radios.Sum(x => x.PreferredWidthFor(avail)) + 12 * (r.Radios.Count - 1);
                    int rx = 0, ry = 0;
                    foreach (var rb in r.Radios)
                    {
                        int rw = total <= avail ? rb.PreferredWidthFor(avail) : avail;
                        int rh = rb.HeightFor(rw);
                        rb.SetBounds(rx, ry, rw, rh);
                        if (total <= avail) rx += rw + 12; else ry += rh + 2;
                        eh = rh;
                    }
                    ew = total <= avail ? total : avail;
                    eh = total <= avail ? r.Radios.Max(x => x.Height) : ry;
                }
                else if (r.Editor is ThemedChoice) { ew = Math.Min(avail, ((ThemedChoice)r.Editor).PreferredWidthFor(EditorMin, EditorMax)); eh = Math.Max(28, r.Editor.Font.Height + 6); }
                else if (r.Editor is ThemedSlider) { ew = Math.Min(EditorMax, avail - valueWidth - 8); eh = Math.Max(28, r.Editor.Font.Height + 6); }
                else if (r.Editor is ThemedStepper) { ew = Math.Min(avail, 132); eh = Math.Max(28, r.Editor.Font.Height + 6); }
                else { ew = Math.Min(avail, EditorMax); eh = r.Editor.PreferredSize.Height; }
                ew = Math.Max(40, ew);
                if (stack)
                {
                    r.Label.SetBounds(Margin, y, lw, lh);
                    r.Editor.SetBounds(Margin, y + lh + 4, ew, eh);
                    h = lh + 4 + eh;
                }
                else
                {
                    r.Label.SetBounds(Margin, y + (lh < eh ? (eh - lh) / 2 : 0), lw, lh);
                    r.Editor.SetBounds(Margin + ex, y, ew, eh);
                    h = Math.Max(lh, eh);
                }
                if (r.Value != null)
                {
                    r.Value.SetBounds(r.Editor.Right + 8, r.Editor.Top, valueWidth, eh);
                    r.Value.TextAlign = ContentAlignment.MiddleLeft;
                }
            }
            if (r.Note != null && !string.IsNullOrEmpty(r.Note.Text))
            {
                int nw = w - indent;
                int nh = TextHeight(r.Note, nw) + 2;
                r.Note.SetBounds(Margin + indent, y + h + 2, nw, nh);
                h += nh + 2;
            }
            else if (r.Note != null) r.Note.SetBounds(Margin, y + h, w, 0);
            return h + 10;
        }

        /// <summary>Lays everything out for a content width; returns the content height.</summary>
        public int Layout(int contentWidth)
        {
            _width = contentWidth;
            int w = Math.Max(120, contentWidth - 2 * Margin);
            int y = Margin;
            foreach (var b in _blocks) y += b(y, w);
            return y + Margin;
        }

        public void Relayout() { var area = _form.Area; if (area != null) area.SetContentHeight(Layout(area.ContentWidth)); }

        /// <summary>Puts current values into the editors (changes made elsewhere, e.g. the window dragged to a new size).</summary>
        public void Sync()
        {
            foreach (var r in Rows.Values)
            {
                var v = S.Get(r.Key);
                var cb = r.Editor as ThemedCheck;
                if (cb != null) { cb.Checked = v == "true"; continue; }
                if (r.Radios != null) { var d = Settings.Def(r.Key); for (int i = 0; i < r.Radios.Count; i++) r.Radios[i].Checked = d.Choices[i] == v; continue; }
                var ch = r.Editor as ThemedChoice;
                if (ch != null) { var choices = r.Key == "screen.windowSize" ? WindowSizes.Steps : Settings.Def(r.Key).Choices; ch.SelectedIndex = Array.IndexOf(choices, v); continue; }
                var sl = r.Editor as ThemedSlider;
                if (sl != null) { sl.Value = S.GetInt(r.Key); r.Value.Text = sl.Value + "%"; continue; }
                var st = r.Editor as ThemedStepper;
                if (st != null) st.Value = S.GetInt(r.Key);
            }
        }

        public void Enable(string key, bool on)
        {
            Row r;
            if (!Rows.TryGetValue(key, out r)) return;
            r.Editor.Enabled = on;
            if (r.Radios != null) foreach (var rb in r.Radios) rb.Enabled = on;
            // labels stay enabled (a disabled WinForms label is drawn embossed in system grey); only their colour dims
            if (r.Label != null) r.Label.ForeColor = on ? _form.Pal.Text : Palette.Mix(_form.Pal.Back, _form.Pal.Text, 0.55);
        }

        public void Recolor(Palette p)
        {
            if (p == null) return;
            foreach (var c in _all) Recolor(c, p);
            foreach (var r in Rows.Values) if (r.Radios != null) foreach (var rb in r.Radios) rb.Pal = p;
        }

        private static void Recolor(Control c, Palette p)
        {
            var tc = c as ThemedControl;
            if (tc != null) { tc.Pal = p; return; }
            var b = c as Button;
            if (b != null) { p.StyleButton(b); if (b.Tag as string == "warn") { b.ForeColor = p.Warning; b.FlatAppearance.BorderColor = p.Warning; } return; }
            var link = c as LinkLabel;
            if (link != null) { link.BackColor = p.Back; link.LinkColor = p.Accent; link.ActiveLinkColor = p.Accent; link.VisitedLinkColor = p.Accent; link.ForeColor = p.Text; return; }
            var l = c as Label;
            if (l != null)
            {
                var tag = l.Tag as string;
                l.BackColor = tag == "band" ? p.Accent : p.Back;
                l.ForeColor = tag == "sub" ? p.Sub : tag == "warn" ? p.Warning : tag == "band" ? p.AccentText : p.Text;
                return;
            }
            var t = c as TextBox;
            if (t != null) { t.BackColor = p.Input; t.ForeColor = p.InputText; return; }
            var cv = c as ChatView;
            if (cv != null) return;   // previews take the colours from their render style
            c.BackColor = p.Back; c.ForeColor = p.Text;
            foreach (Control child in c.Controls) Recolor(child, p);
        }

        public void Dispose()
        {
            foreach (var c in _all) { _host.Controls.Remove(c); c.Dispose(); }
            _all.Clear();
        }
    }
}
