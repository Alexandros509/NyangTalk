using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NyangTalk.App.Rendering
{
    public sealed class TextLine
    {
        public int Start, Length, Width, Top;
    }

    /// <summary>
    /// Word wrapping done by our own code, so the lines that are drawn and the character positions used for mouse
    /// selection always agree (50-SPEC 1장 글자 일부 선택). Break opportunities: spaces and explicit line breaks; a word
    /// longer than the line is split between text elements (never inside a surrogate pair or combining sequence).
    /// </summary>
    public static class TextLayout
    {
        public const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        private static readonly Dictionary<string, int> WidthCache = new Dictionary<string, int>();

        /// <summary>The overload without a device context ignores NoPadding on .NET Framework.</summary>
        public static Size MeasureText(string text, Font font, Size proposed, TextFormatFlags flags)
        {
            using (var context = Graphics.FromHwnd(IntPtr.Zero))
                return TextRenderer.MeasureText(context, text, font, proposed, flags);
        }

        public static int Width(string s, Font f)
        {
            if (s.Length == 0) return 0;
            var key = f.Name + "|" + f.SizeInPoints + "|" + (int)f.Style + "|" + s;
            int w;
            if (WidthCache.TryGetValue(key, out w)) return w;
            w = MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), Flags).Width;
            if (WidthCache.Count > 50000) WidthCache.Clear();
            WidthCache[key] = w;
            return w;
        }

        public static int LineHeight(Font f) { return MeasureText("Ag가", f, new Size(int.MaxValue, int.MaxValue), Flags).Height; }

        /// <summary>Start indexes of text elements, plus text.Length at the end.</summary>
        public static int[] Boundaries(string text)
        {
            var starts = StringInfo.ParseCombiningCharacters(text);
            var r = new int[starts.Length + 1];
            Array.Copy(starts, r, starts.Length);
            r[starts.Length] = text.Length;
            return r;
        }

        public static List<TextLine> Wrap(string text, Font f, int maxWidth)
        {
            var lines = new List<TextLine>();
            text = text ?? "";
            int lh = LineHeight(f), top = 0;
            int pos = 0;
            foreach (var para in text.Split('\n'))
            {
                int paraStart = pos;
                var p = para.EndsWith("\r") ? para.Substring(0, para.Length - 1) : para;
                int i = 0;
                if (p.Length == 0) { lines.Add(new TextLine { Start = paraStart, Length = 0, Width = 0, Top = top }); top += lh; }
                while (i < p.Length)
                {
                    // take as many words as fit
                    int end = i, lastFit = -1;
                    while (end < p.Length)
                    {
                        int next = p.IndexOf(' ', end);
                        next = next < 0 ? p.Length : next + 1;
                        if (Width(p.Substring(i, next - i).TrimEnd(' '), f) <= maxWidth) { lastFit = next; end = next; }
                        else break;
                    }
                    int lineEnd;
                    if (lastFit > i) lineEnd = lastFit;
                    else
                    {
                        // one word wider than the line: split it between text elements
                        var b = Boundaries(p.Substring(i));
                        int k = 1;
                        while (k < b.Length - 1 && Width(p.Substring(i, b[k + 1]), f) <= maxWidth) k++;
                        lineEnd = i + b[Math.Min(k, b.Length - 1)];
                    }
                    var seg = p.Substring(i, lineEnd - i);
                    lines.Add(new TextLine { Start = paraStart + i, Length = lineEnd - i, Width = Width(seg.TrimEnd(' '), f), Top = top });
                    top += lh;
                    i = lineEnd;
                }
                pos += para.Length + 1;
            }
            return lines;
        }

        public static Size Measure(List<TextLine> lines, Font f)
        {
            int w = 0;
            foreach (var l in lines) w = Math.Max(w, l.Width);
            return new Size(w, lines.Count == 0 ? LineHeight(f) : lines[lines.Count - 1].Top + LineHeight(f));
        }

        /// <summary>X offset of character index <paramref name="index"/> inside its line.</summary>
        public static int XOf(string text, TextLine line, int index, Font f)
        {
            int n = Math.Max(0, Math.Min(index, line.Start + line.Length) - line.Start);
            return Width(text.Substring(line.Start, n), f);
        }

        /// <summary>Character index (on a text-element boundary) nearest to a point relative to the text's top-left.</summary>
        public static int IndexAt(string text, List<TextLine> lines, Point p, Font f)
        {
            if (lines.Count == 0) return 0;
            var line = lines[lines.Count - 1];
            foreach (var l in lines) if (p.Y < l.Top + LineHeight(f)) { line = l; break; }
            if (p.Y < 0) line = lines[0];
            var seg = text.Substring(line.Start, line.Length);
            var b = Boundaries(seg);
            int best = 0, bestDist = int.MaxValue;
            foreach (var k in b)
            {
                if (k > seg.TrimEnd(' ', '\r').Length && k != 0) break;
                int d = Math.Abs(Width(seg.Substring(0, k), f) - p.X);
                if (d < bestDist) { bestDist = d; best = k; }
            }
            return line.Start + best;
        }

        public static void Draw(Graphics g, string text, List<TextLine> lines, Font f, Point origin, Color fore)
        {
            foreach (var l in lines)
            {
                var seg = text.Substring(l.Start, l.Length).TrimEnd(' ', '\r');
                if (seg.Length > 0) TextRenderer.DrawText(g, seg, f, new Point(origin.X, origin.Y + l.Top), fore, Flags);
            }
        }

        /// <summary>Word around an index: letters/digits run, otherwise the single text element.</summary>
        public static void WordAt(string text, int index, out int start, out int end)
        {
            index = Math.Max(0, Math.Min(index, text.Length - 1));
            if (text.Length == 0) { start = end = 0; return; }
            Func<char, bool> word = c => char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c);
            if (!word(text[index])) { start = index; end = index + 1; if (char.IsHighSurrogate(text[index]) && end < text.Length) end++; return; }
            start = index; end = index;
            while (start > 0 && word(text[start - 1])) start--;
            while (end < text.Length && word(text[end])) end++;
        }
    }
}
