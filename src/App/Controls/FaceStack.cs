using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.People;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Header participants button (30-SPEC 4.2, 60-SPEC 1장): up to 3 overlapping faces, the member count and a ▾ inside
    /// one outlined pill, so it reads as a button (2026-11-20 user choice "A"). Hover and press tint the pill; Enter or
    /// Space opens the list like a click.
    /// </summary>
    public sealed class FaceStack : Control
    {
        private List<Participant> _faces = new List<Participant>();
        private int _count;
        private bool _hot, _pressed, _keyboardFocus, _mouseDown;
        public RenderStyle Style = new RenderStyle();
        public Func<string, string> AliasOf = id => null;
        public const int PillHeight = 28, Face = 22, FaceStep = 14;

        public FaceStack()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public IList<Participant> Faces { get { return _faces; } }
        public int Count { get { return _count; } }
        public bool Hot { get { return _hot; } }

        public void SetPeople(IList<Participant> people, int count)
        {
            _faces = ParticipantList.HeaderFaces(people);
            _count = count;
            Width = PreferredWidth;
            AccessibleDescription = CountText;
            Invalidate();
        }

        private string CountText { get { return string.Format(Style.L("room.members"), _count); } }
        private int PillSize { get { return HeaderPill.HeightFor(Font); } }
        private int FacesWidth { get { return _faces.Count == 0 ? 0 : Face + (_faces.Count - 1) * FaceStep; } }

        public int PreferredWidth
        {
            get
            {
                return HeaderPill.WidePadding + FacesWidth + 6 + TextLayout.MeasureText(CountText, Font, Size.Empty, TextFormatFlags.NoPadding).Width + 6 + 10 + 9;
            }
        }

        /// <summary>The pill's outline, background and hover colours from the theme.</summary>
        /// <summary>True while the participants panel is open (the pill looks pressed).</summary>
        public bool Pressed { get { return _open; } set { _open = value; Invalidate(); } }
        private bool _open;

        private Color Base { get { return Parent != null ? Parent.BackColor : RenderStyle.C(Style.Theme.Panel); } }

        /// <summary>
        /// The pill takes the header's colour, a step towards the text colour (2026-11-20: theme colour inside, a soft line
        /// lighter or darker than the background so it reads as something to press).
        /// </summary>
        public Color PillBack { get { return RenderStyle.C(Style.Theme.OtherBubble); } }
        public Color PillLine { get { return RenderStyle.C(Style.Theme.Border); } }

        public bool FocusRing { get { return Focused && _keyboardFocus; } }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void WndProc(ref Message m)
        {
            bool down = m.Msg == 0x0201 || m.Msg == 0x0204;
            if (down) _mouseDown = true;
            try { base.WndProc(ref m); } finally { if (down) _mouseDown = false; }
        }

        protected override void OnGotFocus(EventArgs e) { _keyboardFocus = !_mouseDown; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _keyboardFocus = false; Invalidate(); base.OnLostFocus(e); }

        /// <summary>Same as a click (keyboard and checks).</summary>
        public void PerformClick() { OnClick(EventArgs.Empty); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Base);
            PaintContents(e.Graphics, !Enabled ? HeaderPill.DisabledOpacity : _hot ? HeaderPill.HoverOpacity : 1f);
        }

        private void PaintContents(Graphics g, float opacity)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var pal = Palette.From(Style.Theme);
            Func<Color, Color> blend = c => HeaderPill.Composite(Base, c, opacity);
            var fill = blend(PillBack);
            int pillHeight = PillSize;
            int top = Math.Max(0, (Height - pillHeight) / 2);
            var pill = new RectangleF(0.5f, top + 0.5f, Width - 1, pillHeight - 1);
            using (var path = ThemedControl.Round(pill, HeaderPill.Radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(blend(PillLine), FocusRing ? 1.6f : 1f)) g.DrawPath(pen, path);
            }
            int x = HeaderPill.WidePadding;
            using (var f = NyangTalk.App.Platform.Fonts.Create(Style.FontFamily, 7.5f, FontStyle.Bold))
            {
                foreach (var p in _faces)
                {
                    var r = new Rectangle(x, top + (pillHeight - Face) / 2, Face, Face);
                    var c = ColorMath.EnsureTextContrast(Style.Colors.BaseColor(p.SteamId, false) ?? Style.Theme.Accent, new Rgb(255, 255, 255));
                    using (var b = new SolidBrush(blend(RenderStyle.C(c)))) g.FillEllipse(b, r);
                    using (var pen = new Pen(fill, 2)) g.DrawEllipse(pen, r);
                    ChatPainter.DrawCentered(g, DisplayName.Initial(DisplayName.Format(p.Name, AliasOf(p.SteamId), DisplayNameMode.AliasOnly)), f, r, blend(Color.White));
                    x += FaceStep;
                }
            }
            x = HeaderPill.WidePadding + FacesWidth + 6;
            int chevron = Width - 9 - 10;
            TextRenderer.DrawText(g, CountText, Font, new Rectangle(x, top, Math.Max(10, chevron - x - 2), pillHeight), blend(pal.Mid), fill,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            float cx = chevron + 5, cy = top + pillHeight / 2f;
            using (var pen = new Pen(blend(pal.Sub), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }
}
