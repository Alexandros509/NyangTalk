using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Colours of the current theme for window controls (90-SPEC 1.4: background, text, lines, selection, buttons, input
    /// boxes and scroll bars all follow the theme; no Windows default grey).
    /// </summary>
    public sealed class Palette
    {
        public Color Back, Panel, Text, Mid, Sub, Accent, AccentText, Border, Input, InputText, Warning, Hover;
        public bool Dark;

        public static Palette From(Theme th)
        {
            return new Palette
            {
                Back = RenderStyle.C(th.Window), Panel = RenderStyle.C(th.Panel), Text = RenderStyle.C(th.Text), Mid = RenderStyle.C(th.MidText), Sub = RenderStyle.C(th.SubText),
                Accent = RenderStyle.C(th.Accent), AccentText = RenderStyle.C(th.AccentText), Border = RenderStyle.C(th.Border),
                Input = RenderStyle.C(th.OtherBubble), InputText = RenderStyle.C(th.OtherText), Warning = RenderStyle.C(th.Warning),
                Hover = Mix(RenderStyle.C(th.Panel), RenderStyle.C(th.Accent), 0.12), Dark = th.Dark
            };
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Plain buttons drawn by WinForms from these colours (flat style: no system face colour).</summary>
        public void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Panel; b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border; b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Mix(Panel, Accent, 0.25);
        }
    }

    /// <summary>
    /// Tooltips with a delay of about half a second, also shown when a control gets keyboard focus (30-SPEC 3장, 5장).
    /// </summary>
    public sealed class HoverTips : IDisposable
    {
        public const int DelayMs = 500;
        private readonly ToolTip _tip = new ToolTip { InitialDelay = DelayMs, ReshowDelay = 100, AutoPopDelay = 15000, ShowAlways = true };
        private readonly Dictionary<Control, string> _texts = new Dictionary<Control, string>();
        private readonly Timer _focusTimer = new Timer { Interval = DelayMs };
        private Control _focused;

        public HoverTips()
        {
            _focusTimer.Tick += (s, e) =>
            {
                _focusTimer.Stop();
                var c = _focused;
                if (c != null && !c.IsDisposed && c.Focused && c.IsHandleCreated) _tip.Show(Get(c), c, 0, c.Height + 2, 4000);
            };
        }

        public int InitialDelay { get { return _tip.InitialDelay; } }

        public void Set(Control c, string text)
        {
            if (c == null) return;
            bool known = _texts.ContainsKey(c);
            _texts[c] = text;
            if (!_muted.Contains(c)) _tip.SetToolTip(c, text);
            if (c.AccessibleDescription == null || known) c.AccessibleDescription = text;
            if (known) return;
            // keyboard focus only: a click also focuses, and that tooltip would cover the list the click opens
            c.GotFocus += (s, e) => { _focused = c; _focusTimer.Stop(); if (Control.MouseButtons == MouseButtons.None && !_muted.Contains(c)) _focusTimer.Start(); };
            // once pressed, the tooltip stays away until the pointer leaves (it covered the first drop-down choice)
            c.MouseDown += (s, e) => Mute(c);
            c.MouseLeave += (s, e) => Unmute(c);
            c.LostFocus += (s, e) => { if (_focused == c) { _focused = null; _focusTimer.Stop(); if (c.IsHandleCreated) _tip.Hide(c); } };
            c.Disposed += (s, e) => { _texts.Remove(c); _muted.Remove(c); };
        }

        public string Get(Control c) { string t; return c != null && _texts.TryGetValue(c, out t) ? t : null; }

        private readonly HashSet<Control> _muted = new HashSet<Control>();

        private void Mute(Control c)
        {
            _focusTimer.Stop();
            if (!_muted.Add(c)) return;
            if (c.IsHandleCreated) _tip.Hide(c);
            _tip.SetToolTip(c, null);
        }

        private void Unmute(Control c)
        {
            if (!_muted.Remove(c)) return;
            string t;
            if (_texts.TryGetValue(c, out t)) _tip.SetToolTip(c, t);
        }

        /// <summary>The text the tooltip would show now (empty while muted after a click).</summary>
        public string Active(Control c) { return _tip.GetToolTip(c); }

        public void Dispose() { _focusTimer.Dispose(); _tip.Dispose(); }
    }

    /// <summary>Base for the drawn controls: theme colours, focus outline, accessible name = text.</summary>
    public abstract class ThemedControl : Control
    {
        private Palette _pal = Palette.From(Theme.Get("default"));
        protected bool Hot;

        protected ThemedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public Palette Pal { get { return _pal; } set { _pal = value; BackColor = value.Back; ForeColor = value.Text; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (this is ThemedChoice || this is ThemedStepper || this is ThemedSlider) Height = Math.Max(28, Font.Height + 6);
            Invalidate();
        }

        protected Color TextColor { get { return Enabled ? Pal.Text : Palette.Mix(Pal.Back, Pal.Text, 0.5); } }

        protected void DrawFocus(Graphics g, Rectangle r)
        {
            if (!Focused || !ShowFocusCues) return;
            using (var p = new Pen(Pal.Accent, 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Check box (or radio button) whose label wraps instead of being cut (90-SPEC 1.4).</summary>
    public sealed class ThemedCheck : ThemedControl
    {
        private bool _checked;
        public bool Radio;
        public event EventHandler CheckedChanged;
        /// <summary>Asked before a user click changes the value; false keeps it.</summary>
        public Func<bool, bool> BeforeToggle = v => true;
        private const int Box = 16, Gap = 8;

        public ThemedCheck() { AccessibleRole = AccessibleRole.CheckButton; }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked == value) return; _checked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        public int HeightFor(int width)
        {
            var sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(Math.Max(20, width - Box - Gap - 2), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return Math.Max(Box + 6, sz.Height + 6);
        }

        public int PreferredWidthFor(int max)
        {
            var sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            return Math.Min(max, sz.Width + Box + Gap + 6);
        }

        private void UserToggle()
        {
            if (!Enabled) return;
            bool next = Radio ? true : !_checked;
            if (next == _checked) return;
            if (!BeforeToggle(next)) return;
            Checked = next;
        }

        protected override void OnClick(EventArgs e) { Focus(); UserToggle(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { UserToggle(); e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new RectangleF(1.5f, 3.5f, Box - 1, Box - 1);
            var line = Enabled ? (Hot ? Pal.Accent : Pal.Sub) : Palette.Mix(Pal.Back, Pal.Sub, 0.5);
            using (var pen = new Pen(_checked && Enabled ? Pal.Accent : line, 1.5f))
            {
                if (Radio)
                {
                    using (var b = new SolidBrush(Pal.Input)) g.FillEllipse(b, box);
                    g.DrawEllipse(pen, box);
                    if (_checked) using (var b = new SolidBrush(Enabled ? Pal.Accent : line)) g.FillEllipse(b, box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
                }
                else
                {
                    using (var path = Round(box, 3))
                    {
                        using (var b = new SolidBrush(_checked ? (Enabled ? Pal.Accent : line) : Pal.Input)) g.FillPath(b, path);
                        g.DrawPath(pen, path);
                    }
                    if (_checked)
                        using (var p = new Pen(Pal.AccentText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLines(p, new[] { new PointF(box.X + 3.5f, box.Y + 8), new PointF(box.X + 6.5f, box.Y + 11), new PointF(box.X + 12, box.Y + 4.5f) });
                }
            }
            var tr = new Rectangle(Box + Gap, 3, Width - Box - Gap - 2, Height - 3);
            TextRenderer.DrawText(g, Text, Font, tr, TextColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            DrawFocus(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>Value picker for three or more choices: shows the value and a chevron, opens a list on click or Alt+Down.</summary>
    public sealed class ThemedChoice : ThemedControl
    {
        public string[] Items = new string[0];
        private int _index = -1;
        public event Action<int> Picked;   // user choice; the owner decides whether to accept it
        private ContextMenuStrip _menu;

        public ThemedChoice() { AccessibleRole = AccessibleRole.ComboBox; Height = 28; }

        /// <summary>Shown when no item is selected (e.g. the window size after the user dragged it: "직접 조절").</summary>
        public string Placeholder = "";
        public int SelectedIndex { get { return _index; } set { _index = value; AccessibleDescription = SelectedText; Invalidate(); } }
        public string SelectedText { get { return _index >= 0 && _index < Items.Length ? Items[_index] : Placeholder; } }

        public int PreferredWidthFor(int min, int max)
        {
            int w = 0;
            foreach (var it in Items) w = Math.Max(w, TextRenderer.MeasureText(it, Font).Width);
            return Math.Max(min, Math.Min(max, w + 40));
        }

        private void Pick(int i) { if (!Enabled || i < 0 || i >= Items.Length || i == _index) return; var h = Picked; if (h != null) h(i); }

        public void Open()
        {
            if (!Enabled) return;
            if (_menu != null) _menu.Dispose();
            _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, BackColor = Pal.Panel, ForeColor = Pal.Text, Font = Font };
            _menu.Renderer = new ThemedMenuRenderer(Pal);
            for (int i = 0; i < Items.Length; i++)
            {
                int idx = i;
                var item = new ToolStripMenuItem(Items[i]) { Checked = i == _index, BackColor = Pal.Panel, ForeColor = Pal.Text };
                item.Click += (s, e) => Pick(idx);
                _menu.Items.Add(item);
            }
            _menu.MinimumSize = new Size(Width, 0);
            _menu.Show(this, new Point(0, Height));
        }

        protected override void OnClick(EventArgs e) { Focus(); Open(); base.OnClick(e); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && !e.Alt) { Pick(Math.Max(0, _index - 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && !e.Alt) { Pick(Math.Min(Items.Length - 1, _index + 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) { Open(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Round(r, 4))
            {
                using (var b = new SolidBrush(Enabled ? (Hot ? Palette.Mix(Pal.Input, Pal.Accent, 0.06) : Pal.Input) : Pal.Back)) g.FillPath(b, path);
                using (var p = new Pen(Focused ? Pal.Accent : Pal.Border)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, SelectedText, Font, new Rectangle(8, 0, Width - 32, Height), Enabled ? Pal.InputText : Palette.Mix(Pal.Back, Pal.Text, 0.5), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            float cx = Width - 14, cy = Height / 2f;
            using (var p = new Pen(TextColor, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }

        protected override void Dispose(bool disposing) { if (disposing && _menu != null) { _menu.Dispose(); _menu = null; } base.Dispose(disposing); }
    }

    /// <summary>Menu colours from the theme (drop-down lists, the rooms popup, "더 보기").</summary>
    public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Palette _p;
        public ThemedMenuRenderer(Palette p) : base(new Colors(p)) { _p = p; RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? _p.Text : _p.Sub; base.OnRenderItemText(e); }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var p = new Pen(_p.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(p, new[] { new PointF(r.X + 3, r.Y + r.Height / 2f), new PointF(r.X + r.Width / 2f - 1, r.Bottom - 4), new PointF(r.Right - 3, r.Y + 4) });
            }
        }

        private sealed class Colors : ProfessionalColorTable
        {
            private readonly Palette _p;
            public Colors(Palette p) { _p = p; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return _p.Panel; } }
            public override Color MenuBorder { get { return _p.Border; } }
            public override Color MenuItemBorder { get { return _p.Accent; } }
            public override Color MenuItemSelected { get { return _p.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return _p.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return _p.Hover; } }
            public override Color ImageMarginGradientBegin { get { return _p.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return _p.Panel; } }
            public override Color ImageMarginGradientEnd { get { return _p.Panel; } }
            public override Color CheckBackground { get { return _p.Panel; } }
            public override Color CheckSelectedBackground { get { return _p.Hover; } }
            public override Color CheckPressedBackground { get { return _p.Hover; } }
            public override Color SeparatorDark { get { return _p.Border; } }
            public override Color SeparatorLight { get { return _p.Border; } }
        }
    }

    /// <summary>Slider in theme colours. <see cref="Sliding"/> fires while it moves, <see cref="Committed"/> on release.</summary>
    public sealed class ThemedSlider : ThemedControl
    {
        public int Minimum, Maximum = 100, Step = 1;
        private int _value;
        private bool _drag;
        public event Action<int> Sliding;
        public event Action<int> Committed;

        public ThemedSlider() { AccessibleRole = AccessibleRole.Slider; Height = 28; }

        public int Value { get { return _value; } set { _value = Snap(value); AccessibleDescription = _value.ToString(); Invalidate(); } }

        private int Snap(int v)
        {
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            return Minimum + (int)Math.Round((v - Minimum) / (double)Step) * Step;
        }

        private float Track0 { get { return 9; } }
        private float Track1 { get { return Width - 9; } }

        private void MoveTo(int v, bool commit)
        {
            v = Snap(v);
            if (v != _value) { Value = v; var h = Sliding; if (h != null) h(v); }
            if (commit) { var c = Committed; if (c != null) c(_value); }
        }

        private int ValueAt(int x) { return Minimum + (int)Math.Round((x - Track0) / Math.Max(1, Track1 - Track0) * (Maximum - Minimum)); }

        protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; Focus(); _drag = true; Capture = true; MoveTo(ValueAt(e.X), false); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) MoveTo(ValueAt(e.X), false); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { if (_drag) { _drag = false; Capture = false; MoveTo(ValueAt(e.X), true); } base.OnMouseUp(e); }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: MoveTo(_value - Step, false); e.Handled = true; break;
                case Keys.Right: case Keys.Up: MoveTo(_value + Step, false); e.Handled = true; break;
                case Keys.Home: MoveTo(Minimum, false); e.Handled = true; break;
                case Keys.End: MoveTo(Maximum, false); e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) MoveTo(_value, true);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f;
            float x = Track0 + (Track1 - Track0) * (_value - Minimum) / Math.Max(1f, Maximum - Minimum);
            var on = Enabled ? Pal.Accent : Pal.Sub;
            using (var b = new SolidBrush(Pal.Border)) g.FillRectangle(b, Track0, cy - 2, Track1 - Track0, 4);
            using (var b = new SolidBrush(on)) g.FillRectangle(b, Track0, cy - 2, x - Track0, 4);
            using (var b = new SolidBrush(Pal.Input)) g.FillEllipse(b, x - 8, cy - 8, 16, 16);
            using (var p = new Pen(on, 2)) g.DrawEllipse(p, x - 7, cy - 7, 14, 14);
            DrawFocus(g, new Rectangle(0, 0, Width, Height));
        }
    }

    /// <summary>Number with − and + (and arrow keys / wheel), in theme colours.</summary>
    public sealed class ThemedStepper : ThemedControl
    {
        public int Minimum, Maximum = 100, Step = 1;
        public string Unit = "";
        private int _value;
        public event Action<int> ValueChanged;

        public ThemedStepper() { AccessibleRole = AccessibleRole.SpinButton; Height = 28; Width = 132; }

        public int Value { get { return _value; } set { _value = Math.Max(Minimum, Math.Min(Maximum, value)); AccessibleDescription = _value.ToString(); Invalidate(); } }

        private void Change(int v)
        {
            if (!Enabled) return;
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            if (v == _value) return;
            Value = v;
            var h = ValueChanged; if (h != null) h(v);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.X < 30) Change(_value - Step); else if (e.X > Width - 30) Change(_value + Step);
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { Change(_value + Math.Sign(e.Delta) * Step); base.OnMouseWheel(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) { Change(_value + Step); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) { Change(_value - Step); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 4))
            {
                using (var b = new SolidBrush(Enabled ? Pal.Input : Pal.Back)) g.FillPath(b, path);
                using (var p = new Pen(Focused ? Pal.Accent : Pal.Border)) g.DrawPath(p, path);
            }
            var c = Enabled ? Pal.InputText : Palette.Mix(Pal.Back, Pal.Text, 0.5);
            using (var p = new Pen(Pal.Border)) { g.DrawLine(p, 30, 4, 30, Height - 4); g.DrawLine(p, Width - 30, 4, Width - 30, Height - 4); }
            using (var p = new Pen(c, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cy = Height / 2f;
                g.DrawLine(p, 10, cy, 20, cy);
                g.DrawLine(p, Width - 20, cy, Width - 10, cy); g.DrawLine(p, Width - 15, cy - 5, Width - 15, cy + 5);
            }
            TextRenderer.DrawText(g, _value + Unit, Font, new Rectangle(30, 0, Width - 60, Height), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Connection state: a coloured dot and one sentence (90-SPEC 1.4 연결 ①).</summary>
    public sealed class StatusDot : ThemedControl
    {
        public int State;   // 2 connected (green), 1 waiting (amber), 0 off (grey)

        public StatusDot() { TabStop = false; AccessibleRole = AccessibleRole.StaticText; }

        public int HeightFor(int width)
        {
            return Math.Max(22, TextRenderer.MeasureText(Text ?? "", Font, new Size(Math.Max(20, width - 22), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = State == 2 ? Color.FromArgb(0x1F, 0x9D, 0x55) : State == 1 ? Color.FromArgb(0xC9, 0x8A, 0x00) : Pal.Sub;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, 2, 5, 11, 11);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(22, 2, Width - 22, Height - 2), Pal.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Category list on the left of the settings window: button look, current one bold on a coloured back.</summary>
    public sealed class CategoryList : ThemedControl
    {
        public readonly List<string> Items = new List<string>();
        private int _index, _hover = -1;
        public event Action<int> SelectedChanged;
        public const int ItemHeight = 38;

        public CategoryList() { AccessibleRole = AccessibleRole.List; }

        public int SelectedIndex
        {
            get { return _index; }
            set { if (value < 0 || value >= Items.Count || value == _index) return; _index = value; AccessibleDescription = Items[value]; Invalidate(); var h = SelectedChanged; if (h != null) h(value); }
        }

        public Rectangle ItemRect(int i) { return new Rectangle(6, 8 + i * ItemHeight, Width - 12, ItemHeight - 4); }

        private int At(Point p) { for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != _hover) { _hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); int i = At(e.Location); if (i >= 0) SelectedIndex = i; base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, _index - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(Items.Count - 1, _index + 1); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Count; i++)
            {
                var r = ItemRect(i);
                bool sel = i == _index;
                if (sel || i == _hover)
                    using (var path = Round(r, 6))
                    using (var b = new SolidBrush(sel ? Pal.Accent : Pal.Hover)) g.FillPath(b, path);
                using (var f = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(g, Items[i], f, new Rectangle(r.X + 10, r.Y, r.Width - 14, r.Height), sel ? Pal.AccentText : Pal.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                if (sel && Focused && ShowFocusCues) using (var p = new Pen(Pal.AccentText, 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, r.X + 3, r.Y + 3, r.Width - 7, r.Height - 7);
            }
        }
    }

    /// <summary>
    /// Vertical scrolling area with a thin theme-coloured scroll bar (system scroll bars would stay grey). There is never a
    /// horizontal scroll bar: the content is laid out to the viewport width.
    /// </summary>
    public sealed class ScrollArea : Control
    {
        public readonly Panel Content = new Panel();
        private int _offset, _drag = -1, _dragOffset;
        private Palette _pal = Palette.From(Theme.Get("default"));
        public const int BarWidth = 10;
        public event Action Scrolled;

        public ScrollArea()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Controls.Add(Content);
            Content.Location = Point.Empty;
        }

        public Palette Pal { get { return _pal; } set { _pal = value; BackColor = value.Back; Content.BackColor = value.Back; Invalidate(); } }
        public int ContentWidth { get { return Math.Max(50, ClientSize.Width - BarWidth - 4); } }
        public int Offset { get { return _offset; } }
        public int MaxOffset { get { return Math.Max(0, Content.Height - ClientSize.Height); } }
        public bool BarVisible { get { return MaxOffset > 0; } }

        public void SetContentHeight(int h) { Content.Size = new Size(ContentWidth, h); ScrollTo(_offset); }

        public void ScrollTo(int y)
        {
            _offset = Math.Max(0, Math.Min(MaxOffset, y));
            Content.Location = new Point(0, -_offset);
            Invalidate();
            var h = Scrolled; if (h != null) h();
        }

        public void ScrollBy(int dy) { ScrollTo(_offset + dy); }

        /// <summary>Keeps a focused control visible (Tab moves through the page).</summary>
        public void EnsureVisible(Control c)
        {
            if (c == null || c.Parent == null || !c.IsHandleCreated || !IsHandleCreated) return;
            var top = Content.PointToClient(c.Parent.PointToScreen(c.Location)).Y;
            if (top < _offset) ScrollTo(top - 8);
            else if (top + c.Height > _offset + ClientSize.Height) ScrollTo(top + c.Height - ClientSize.Height + 8);
        }

        private Rectangle Thumb()
        {
            int h = ClientSize.Height;
            if (Content.Height <= h) return Rectangle.Empty;
            int th = Math.Max(24, h * h / Content.Height);
            int y = (int)((h - th) * (_offset / (double)MaxOffset));
            return new Rectangle(ClientSize.Width - BarWidth, y, BarWidth - 2, th);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { ScrollBy(-e.Delta / 120 * 48); base.OnMouseWheel(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var t = Thumb();
            if (t.IsEmpty || e.X < ClientSize.Width - BarWidth) return;
            if (t.Contains(e.Location)) { _drag = e.Y; _dragOffset = _offset; Capture = true; }
            else ScrollBy(e.Y < t.Y ? -ClientSize.Height : ClientSize.Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag < 0) return;
            var t = Thumb();
            int room = Math.Max(1, ClientSize.Height - t.Height);
            ScrollTo(_dragOffset + (int)((e.Y - _drag) * (MaxOffset / (double)room)));
        }

        protected override void OnMouseUp(MouseEventArgs e) { _drag = -1; Capture = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_pal.Back);
            var t = Thumb();
            if (t.IsEmpty) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = ThemedControl.Round(t, 4))
            using (var b = new SolidBrush(Palette.Mix(_pal.Back, _pal.Sub, 0.55))) e.Graphics.FillPath(b, path);
        }
    }
}
