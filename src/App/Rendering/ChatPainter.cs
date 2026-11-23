using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NyangTalk.Domain.People;

namespace NyangTalk.App.Rendering
{
    public static class ChatPainter
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0) { p.AddRectangle(r); return p; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Paint(Graphics g, Row row, int top, int width, RenderStyle s, Measurer m, Func<string, Image> portrait, bool selected, bool highlighted, Func<string, Image> picture = null, int selStart = -1, int selEnd = -1)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var th = s.Theme;
            if (selected || highlighted)
            {
                using (var b = new SolidBrush(Color.FromArgb(highlighted ? 70 : 40, RenderStyle.C(th.Accent))))
                    g.FillRectangle(b, 0, top, width, row.Height);
            }
            foreach (var p in row.Parts)
            {
                var r = p.Rect; r.Offset(0, top);
                switch (p.Kind)
                {
                    case PartKind.Bubble:
                        if (p.Back.IsEmpty) break;
                        using (var path = Rounded(r, s.Radius))
                        {
                            if (s.OutlineOnly)
                            {
                                using (var pen = new Pen(RenderStyle.C(th.Border))) g.DrawPath(pen, path);
                                if (p.Back != RenderStyle.C(th.Window)) using (var b = new SolidBrush(p.Back)) g.FillPath(b, path);
                            }
                            else
                            {
                                using (var b = new SolidBrush(p.Back)) g.FillPath(b, path);
                                if (s.Shape == ChatShape.Card || s.Preset == DesignPreset.Business)
                                    using (var pen = new Pen(RenderStyle.C(th.Border))) g.DrawPath(pen, path);
                            }
                        }
                        if (s.Tail && row.Kind != RowKind.More)
                        {
                            var pts = row.Mine
                                ? new[] { new Point(r.Right - 2, r.Y + 8), new Point(r.Right + 6, r.Y + 4), new Point(r.Right - 2, r.Y + 16) }
                                : new[] { new Point(r.X + 2, r.Y + 8), new Point(r.X - 6, r.Y + 4), new Point(r.X + 2, r.Y + 16) };
                            using (var b = new SolidBrush(p.Back)) g.FillPolygon(b, pts);
                        }
                        break;
                    case PartKind.Image:
                        var pic = picture == null ? null : picture(p.Text);
                        if (pic != null) g.DrawImage(pic, r);
                        break;
                    case PartKind.Avatar:
                        var img = portrait == null ? null : portrait(row.SenderId);
                        if (img != null)
                        {
                            if (s.CatCircle) using (var clip = new GraphicsPath()) { clip.AddEllipse(r); g.SetClip(clip); g.DrawImage(img, r); g.ResetClip(); }
                            else g.DrawImage(img, r);
                        }
                        else
                        {
                            var bc = s.Colors.BaseColor(row.SenderId, row.Mine) ?? th.Accent;
                            var fill = ColorMath.EnsureTextContrast(bc, new Rgb(255, 255, 255));
                            using (var b = new SolidBrush(RenderStyle.C(fill))) g.FillEllipse(b, r);
                            DrawCentered(g, p.Text, m.Font(s, true, true), r, Color.White);
                        }
                        break;
                    case PartKind.Button:
                        using (var path = Rounded(r, Math.Min(8, s.Radius == 0 ? 3 : s.Radius)))
                        {
                            using (var b = new SolidBrush(p.Back)) g.FillPath(b, path);
                            using (var pen = new Pen(RenderStyle.C(th.Border))) g.DrawPath(pen, path);
                        }
                        TextRenderer.DrawText(g, p.Text, m.Font(s, false, true), r, p.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        break;
                    case PartKind.Center:
                        if (!p.Back.IsEmpty) using (var path = Rounded(r, r.Height / 2)) using (var b = new SolidBrush(p.Back)) g.FillPath(b, path);
                        TextRenderer.DrawText(g, p.Text, m.Font(s, false, true), r, p.Fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                        break;
                    default:
                        var font = m.Font(s, p.Bold, p.Small);
                        if (p.Lines == null) { TextRenderer.DrawText(g, p.Text, font, r, p.Fore, Measurer.DrawFlags); break; }
                        if (selStart >= 0 && selEnd > selStart)
                        {
                            int lh = TextLayout.LineHeight(font);
                            using (var hb = new SolidBrush(Color.FromArgb(110, 51, 153, 255)))
                                foreach (var l in p.Lines)
                                {
                                    int a = Math.Max(selStart, l.Start), b = Math.Min(selEnd, l.Start + l.Length);
                                    if (b <= a) continue;
                                    int x0 = TextLayout.XOf(p.Text, l, a, font), x1 = TextLayout.XOf(p.Text, l, b, font);
                                    g.FillRectangle(hb, r.X + x0, r.Y + l.Top, Math.Max(2, x1 - x0), lh);
                                }
                        }
                        TextLayout.Draw(g, p.Text, p.Lines, font, r.Location, p.Fore);
                        break;
                }
            }
        }

        /// <summary>
        /// Draws a short text (an initial) with its visible shape centred in <paramref name="r"/>. Text drawing centres
        /// the font's line box, whose space above and below the letter differs per font (Pretendard, Noto Sans KR), so
        /// the letter looked a little off-centre in the circle.
        /// </summary>
        public static void DrawCentered(Graphics g, string text, Font font, RectangleF r, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var path = new GraphicsPath())
            {
                float em = font.SizeInPoints * g.DpiY / 72f;
                path.AddString(text, font.FontFamily, (int)font.Style, em, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                if (b.Width <= 0 || b.Height <= 0) return;
                using (var m = new Matrix())
                {
                    m.Translate(r.X + r.Width / 2f - (b.X + b.Width / 2f), r.Y + r.Height / 2f - (b.Y + b.Height / 2f));
                    path.Transform(m);
                }
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
                g.SmoothingMode = old;
            }
        }
    }
}
