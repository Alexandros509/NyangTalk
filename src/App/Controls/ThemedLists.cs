using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Vertical scroll bar drawn in theme colours (90-SPEC 1.6, 2026-11-20): dark track and light thumb in dark mode, theme
    /// colours in light mode, never the system's light grey. Same value model as <see cref="VScrollBar"/>: the largest
    /// value is Maximum − LargeChange + 1. Wheel over the bar, dragging the thumb and clicking the track all scroll.
    /// </summary>
    public sealed class ThemedScrollBar : Control
    {
        private int _min, _max = 100, _large = 10, _small = 1, _value;
        private int _drag = -1, _dragValue;
        private bool _hot;
        private Palette _pal = Palette.From(Theme.Get("default"));
        public const int BarWidth = 12;
        public event EventHandler ValueChanged;

        public ThemedScrollBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Width = BarWidth;
            AccessibleRole = AccessibleRole.ScrollBar;
        }

        public Palette Pal { get { return _pal; } set { _pal = value; Invalidate(); } }
        public int Minimum { get { return _min; } set { _min = value; Clamp(); } }
        public int Maximum { get { return _max; } set { _max = value; Clamp(); } }
        public int LargeChange { get { return _large; } set { _large = Math.Max(1, value); Clamp(); } }
        public int SmallChange { get { return _small; } set { _small = Math.Max(1, value); } }
        public int MaxValue { get { return Math.Max(_min, _max - _large + 1); } }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_min, Math.Min(MaxValue, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        private void Clamp() { if (_value > MaxValue || _value < _min) Value = _value; Invalidate(); }

        public Rectangle Thumb()
        {
            int range = _max - _min + 1;
            int h = ClientSize.Height;
            if (!Enabled || range <= _large || h <= 0) return Rectangle.Empty;
            int th = Math.Max(24, (int)((long)h * _large / range));
            int y = (int)((h - th) * (long)(_value - _min) / Math.Max(1, MaxValue - _min));
            return new Rectangle(2, y, Width - 4, th);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            var t = Thumb();
            if (t.IsEmpty) return;
            if (t.Contains(e.Location)) { _drag = e.Y; _dragValue = _value; Capture = true; }
            else Value = _value + (e.Y < t.Y ? -_large : _large);   // click on the track: one page
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag < 0) return;
            var t = Thumb();
            int room = Math.Max(1, ClientSize.Height - t.Height);
            Value = _dragValue + (int)((long)(e.Y - _drag) * (MaxValue - _min) / room);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = -1; Capture = false; }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _value - Math.Sign(e.Delta) * _small * 3;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Palette.Mix(_pal.Back, _pal.Panel, 0.6));
            var t = Thumb();
            if (t.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Palette.Mix(_pal.Back, _pal.Sub, _drag >= 0 ? 0.85 : _hot ? 0.7 : 0.55);
            using (var path = ThemedControl.Round(t, 4))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }
    }

    /// <summary>
    /// One-column list in theme colours with a <see cref="ThemedScrollBar"/> (saved rooms, participants, search results).
    /// Keyboard: Up/Down, Page Up/Down, Home/End move the selection; Enter and Delete reach the owner's KeyDown.
    /// </summary>
    public sealed class ThemedList : Control
    {
        public readonly List<string> Items = new List<string>();
        public readonly ThemedScrollBar Bar = new ThemedScrollBar();
        private int _selected = -1, _hover = -1, _top;
        private bool _muted;
        private Palette _pal = Palette.From(Theme.Get("default"));
        public event EventHandler SelectedIndexChanged;
        /// <summary>Draws unselected items in the secondary (grey) colour, e.g. for the last known participant list while disconnected (95-SPEC 2.8).</summary>
        public bool Muted { get { return _muted; } set { if (_muted == value) return; _muted = value; Invalidate(); } }

        public ThemedList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            Bar.Dock = DockStyle.Right;
            Bar.ValueChanged += (s, e) => { _top = Bar.Value; Invalidate(); };
            Controls.Add(Bar);
        }

        public Palette Pal { get { return _pal; } set { _pal = value; Bar.Pal = value; BackColor = value.Panel; ForeColor = value.Text; Invalidate(); } }
        public int ItemHeight { get { return Font.Height + 10; } }
        public int TopPixel { get { return _top; } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = Items.Count == 0 ? -1 : Math.Max(-1, Math.Min(Items.Count - 1, value));
                if (v == _selected) return;
                _selected = v;
                if (v >= 0) { AccessibleDescription = Items[v]; EnsureVisible(v); }
                Invalidate();
                var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void BeginUpdate() { }
        public void EndUpdate() { UpdateBar(); if (_selected >= Items.Count) _selected = -1; Invalidate(); }

        public int IndexFromPoint(Point p)
        {
            int i = (p.Y + _top) / ItemHeight;
            return p.X < ClientSize.Width - (Bar.Visible ? Bar.Width : 0) && i >= 0 && i < Items.Count ? i : -1;
        }

        private void UpdateBar()
        {
            int total = Items.Count * ItemHeight;
            Bar.Minimum = 0;
            Bar.Maximum = Math.Max(0, total - 1);
            Bar.LargeChange = Math.Max(1, ClientSize.Height);
            Bar.SmallChange = ItemHeight;
            Bar.Visible = total > ClientSize.Height;
            Bar.Enabled = Bar.Visible;
            if (!Bar.Visible) Bar.Value = 0;
            _top = Bar.Visible ? Bar.Value : 0;
        }

        private void EnsureVisible(int i)
        {
            int y = i * ItemHeight;
            if (y < _top) Bar.Value = y;
            else if (y + ItemHeight > _top + ClientSize.Height) Bar.Value = y + ItemHeight - ClientSize.Height;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateBar(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateBar(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = IndexFromPoint(e.Location);
            if (i >= 0) SelectedIndex = i;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = IndexFromPoint(e.Location); if (h != _hover) { _hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Bar.Visible) Bar.Value = _top - Math.Sign(e.Delta) * ItemHeight * 3; }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode) { case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true; }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(1, ClientSize.Height / ItemHeight - 1);
            switch (e.KeyCode)
            {
                case Keys.Up: SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; break;
                case Keys.Down: SelectedIndex = _selected + 1; e.Handled = true; break;
                case Keys.PageUp: SelectedIndex = Math.Max(0, _selected - page); e.Handled = true; break;
                case Keys.PageDown: SelectedIndex = Math.Max(0, _selected) + page; e.Handled = true; break;
                case Keys.Home: SelectedIndex = 0; e.Handled = true; break;
                case Keys.End: SelectedIndex = Items.Count - 1; e.Handled = true; break;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_pal.Panel);
            int ih = ItemHeight, w = ClientSize.Width - (Bar.Visible ? Bar.Width : 0);
            int first = _top / ih;
            for (int i = first; i < Items.Count; i++)
            {
                int y = i * ih - _top;
                if (y > ClientSize.Height) break;
                var r = new Rectangle(0, y, w, ih);
                bool sel = i == _selected;
                if (sel || i == _hover) using (var b = new SolidBrush(sel ? _pal.Accent : _pal.Hover)) g.FillRectangle(b, r);
                TextRenderer.DrawText(g, Items[i], Font, new Rectangle(8, y, w - 12, ih), sel ? _pal.AccentText : (_muted ? _pal.Sub : _pal.Text), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (sel && Focused && ShowFocusCues) using (var p = new Pen(_pal.AccentText) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 2, y + 2, w - 5, ih - 5);
            }
        }
    }
}
