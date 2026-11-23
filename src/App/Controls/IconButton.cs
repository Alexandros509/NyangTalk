using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NyangTalk.App.Controls
{
    /// <summary>Header metrics and grouped opacity from the user-approved design.</summary>
    public static class HeaderPill
    {
        public const int Height = 28, Radius = 8, FontPixels = 11, WidePadding = 9, NarrowPadding = 6;
        public const int WideGap = 6, NarrowGap = 4;
        public const float HoverOpacity = 0.8f, DisabledOpacity = 0.4f;
        // Preserve the reference's minimum metrics; larger body settings enlarge the header without shrinking its text.
        public static float FontPoints(float bodyPoints) { return (FontPixels + Math.Max(0, bodyPoints - 9)) * 72f / 96f; }
        public static int HeightFor(Font font) { return Math.Max(Height, font.Height + 10); }
        public static Color Composite(Color background, Color colour, float opacity)
        {
            return Color.FromArgb((int)Math.Round(background.R + (colour.R - background.R) * opacity),
                (int)Math.Round(background.G + (colour.G - background.G) * opacity),
                (int)Math.Round(background.B + (colour.B - background.B) * opacity));
        }
        public static int Width(string text, Font font, bool narrow)
        {
            return NyangTalk.App.Rendering.TextLayout.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width
                + 2 * (narrow ? NarrowPadding : WidePadding) + 2;
        }
        public static void Draw(Graphics target, Rectangle bounds, Font font, string text, Color fill, Color border,
            Color foreground, float opacity, bool focused, Color backdrop)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            fill = Composite(backdrop, fill, opacity);
            border = Composite(backdrop, border, opacity);
            foreground = Composite(backdrop, foreground, opacity);
            using (var path = ThemedControl.Round(new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1), Radius))
            {
                using (var b = new SolidBrush(fill)) target.FillPath(b, path);
                using (var p = new Pen(border, 1f)) target.DrawPath(p, path);
            }
            // GDI draws on the destination HDC at its actual DPI. No intermediate 96-DPI bitmap or scaled glyphs.
            var inner = Rectangle.Inflate(bounds, -Radius, -2);
            TextRenderer.DrawText(target, text, font, inner, foreground, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
            if (focused)
                using (var p = new Pen(foreground, 1f) { DashStyle = DashStyle.Dot })
                using (var path = ThemedControl.Round(new RectangleF(bounds.X + 2.5f, bounds.Y + 2.5f, bounds.Width - 5, bounds.Height - 5), Radius - 2))
                    target.DrawPath(p, path);
        }
    }

    public enum IconKind { None, Search, Gear, Menu, Refresh, Close, Up, Down, ChevronLeft, ChevronRight, Send, Rooms, More }

    /// <summary>
    /// Button with a simple line icon drawn in code (no icon files, so nothing to license; 30-SPEC 7.1).
    /// Text is kept empty for layout; the accessible name carries the meaning (30-SPEC 5장).
    /// </summary>
    public sealed class IconButton : Button
    {
        public IconKind Icon;
        public Color IconColor = Color.FromArgb(0x33, 0x33, 0x33);
        public bool Round;
        /// <summary>When true, draws a rounded bordered pill/chip block with <see cref="PillText"/> instead of the line icon.</summary>
        public bool Pill;
        public string PillText;
        public Color PillFill = Color.Empty, PillBorder = Color.Empty, PillHover = Color.Empty, PillDown = Color.Empty;
        private bool _hot, _down;
        public bool PillPressed { get { return _down; } }
        public float PillVisualOpacity { get { return !Enabled ? HeaderPill.DisabledOpacity : _hot ? HeaderPill.HoverOpacity : 1f; } }

        public IconButton(IconKind icon)
        {
            Icon = icon;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Size = new Size(32, 30);
            Text = "";
        }

        // ---- focus: no frame after a mouse click; a quiet ring only when focus came from the keyboard (Tab) ----
        private bool _mouseDown, _keyboardFocus;

        /// <summary>True while the button shows its keyboard focus ring.</summary>
        public bool FocusRingVisible { get { return Focused && _keyboardFocus; } }

        protected override void WndProc(ref Message m)
        {
            bool down = m.Msg == 0x0201 || m.Msg == 0x0203 || m.Msg == 0x0204;   // left down, double click, right down
            if (down) _mouseDown = true;
            try { base.WndProc(ref m); }
            finally { if (down) _mouseDown = false; }
        }

        protected override void OnGotFocus(EventArgs e) { _keyboardFocus = !_mouseDown; base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { _keyboardFocus = false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _keyboardFocus = false; _down = true; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hot = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) _down = true; base.OnKeyDown(e); Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs e) { _down = false; base.OnKeyUp(e); Invalidate(); }

        /// <summary>WinForms' own dotted/black focus frame is never drawn; <see cref="OnPaint"/> draws the ring instead.</summary>
        protected override bool ShowFocusCues { get { return false; } }

        /// <summary>Never shown as the window's default button (that draws a dark frame as well).</summary>
        public override void NotifyDefault(bool value) { base.NotifyDefault(false); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Round)
            {
                using (var p = new GraphicsPath()) { p.AddEllipse(0, 0, Width, Height); Region = new Region(p); }
            }
            else Region = null;
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            var g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!Enabled && Text.Length > 0 && !Pill)
            {
                // Native disabled Button text ignores ForeColor and can become black on a dark theme.
                g.Clear(BackColor);
                if (FlatAppearance.BorderSize > 0)
                    using (var p = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize))
                        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor, BackColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                return;
            }
            if (Pill && !string.IsNullOrEmpty(PillText))
            {
                g.Clear(BackColor);
                HeaderPill.Draw(g, ClientRectangle, Font, PillText, PillFill.IsEmpty ? BackColor : PillFill,
                    PillBorder.IsEmpty ? IconColor : PillBorder, ForeColor, PillVisualOpacity, FocusRingVisible, BackColor);
                return;
            }
            if (FocusRingVisible)
                using (var ring = new Pen(Color.FromArgb(150, Text.Length > 0 ? ForeColor : IconColor), 1.5f))
                using (var path = NyangTalk.App.Controls.ThemedControl.Round(new RectangleF(2.5f, 2.5f, Width - 5, Height - 5), Round ? Math.Min(Width, Height) / 2f - 3 : 5))
                    g.DrawPath(ring, path);
            if (Text.Length > 0 || Icon == IconKind.None) return; // text buttons (e.g. "보내기") draw no icon
            float s = Math.Min(Width, Height) * 0.5f;
            float cx = Width / 2f, cy = Height / 2f;
            var color = Enabled ? IconColor : Color.FromArgb(120, IconColor);
            using (var pen = new Pen(color, Math.Max(1.6f, s / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                float h = s / 2f;
                switch (Icon)
                {
                    case IconKind.Search:
                        g.DrawEllipse(pen, cx - h, cy - h, h * 1.5f, h * 1.5f);
                        g.DrawLine(pen, cx + h * 0.35f, cy + h * 0.35f, cx + h, cy + h);
                        break;
                    case IconKind.Gear:
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, cx + (float)Math.Cos(a) * h * 0.65f, cy + (float)Math.Sin(a) * h * 0.65f, cx + (float)Math.Cos(a) * h, cy + (float)Math.Sin(a) * h);
                        }
                        g.DrawEllipse(pen, cx - h * 0.6f, cy - h * 0.6f, h * 1.2f, h * 1.2f);
                        g.FillEllipse(brush, cx - h * 0.2f, cy - h * 0.2f, h * 0.4f, h * 0.4f);
                        break;
                    case IconKind.Menu:
                        for (int i = -1; i <= 1; i++) g.DrawLine(pen, cx - h, cy + i * h * 0.6f, cx + h, cy + i * h * 0.6f);
                        break;
                    case IconKind.Refresh:
                        g.DrawArc(pen, cx - h, cy - h, 2 * h, 2 * h, -60, 290);
                        g.FillPolygon(brush, new[] { new PointF(cx + h * 0.5f, cy - h * 1.25f), new PointF(cx + h * 1.1f, cy - h * 0.55f), new PointF(cx + h * 0.2f, cy - h * 0.45f) });
                        break;
                    case IconKind.Close:
                        g.DrawLine(pen, cx - h * 0.7f, cy - h * 0.7f, cx + h * 0.7f, cy + h * 0.7f);
                        g.DrawLine(pen, cx + h * 0.7f, cy - h * 0.7f, cx - h * 0.7f, cy + h * 0.7f);
                        break;
                    case IconKind.Up: g.DrawLines(pen, new[] { new PointF(cx - h * 0.8f, cy + h * 0.4f), new PointF(cx, cy - h * 0.4f), new PointF(cx + h * 0.8f, cy + h * 0.4f) }); break;
                    case IconKind.Down: g.DrawLines(pen, new[] { new PointF(cx - h * 0.8f, cy - h * 0.4f), new PointF(cx, cy + h * 0.4f), new PointF(cx + h * 0.8f, cy - h * 0.4f) }); break;
                    case IconKind.ChevronLeft: g.DrawLines(pen, new[] { new PointF(cx + h * 0.4f, cy - h * 0.8f), new PointF(cx - h * 0.4f, cy), new PointF(cx + h * 0.4f, cy + h * 0.8f) }); break;
                    case IconKind.ChevronRight: g.DrawLines(pen, new[] { new PointF(cx - h * 0.4f, cy - h * 0.8f), new PointF(cx + h * 0.4f, cy), new PointF(cx - h * 0.4f, cy + h * 0.8f) }); break;
                    case IconKind.Rooms:   // two speech bubbles: saved conversations
                        g.DrawRectangle(pen, cx - h, cy - h * 0.9f, h * 1.3f, h * 0.95f);
                        g.DrawLines(pen, new[] { new PointF(cx - h * 0.1f, cy + h * 0.35f), new PointF(cx + h, cy + h * 0.35f), new PointF(cx + h, cy - h * 0.45f), new PointF(cx + h * 0.55f, cy - h * 0.45f) });
                        g.DrawLine(pen, cx + h * 0.55f, cy + h * 0.35f, cx + h * 0.75f, cy + h * 0.85f);
                        break;
                    case IconKind.More:
                        for (int i = -1; i <= 1; i++) g.FillEllipse(brush, cx + i * h * 0.75f - 1.8f, cy - 1.8f, 3.6f, 3.6f);
                        break;
                    case IconKind.Send:
                        g.FillPolygon(brush, new[] { new PointF(cx - h, cy - h * 0.9f), new PointF(cx + h, cy), new PointF(cx - h, cy + h * 0.9f), new PointF(cx - h * 0.5f, cy) });
                        break;
                }
            }
        }
    }
}
