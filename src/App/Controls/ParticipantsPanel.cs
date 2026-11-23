using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NyangTalk.App.Platform;
using NyangTalk.Domain.Lobby;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Participants as a side panel on the right of the chat, like a messenger (2026-11-20 user request): opened and closed
    /// with the header's people pill. Clicking a name (or Enter) opens that person's menu (nickname, colour).
    /// </summary>
    public sealed class ParticipantsPanel : Panel
    {
        public const int PanelWidth = 240;
        public readonly Label Title = new Label();
        public readonly IconButton CloseButton = new IconButton(IconKind.Close);
        public readonly ThemedList List = new ThemedList();
        public readonly Label Hint = new Label();
        public readonly Label Note = new Label();
        public readonly Panel Line = new Panel();
        public readonly ScrollArea Body = new ScrollArea();
        private List<Participant> _people = new List<Participant>();
        private Font _titleFont;
        private Palette _pal;
        private bool _layingOut;
        public bool Stale { get; private set; }
        public event Action<Participant, Point> PersonMenu;

        public ParticipantsPanel()
        {
            Width = PanelWidth;
            Visible = false;
            Title.AutoSize = false; Title.TextAlign = ContentAlignment.MiddleLeft;
            CloseButton.Size = new Size(32, 30);
            Hint.AutoSize = false;
            Note.AutoSize = false; Note.Visible = false;
            Title.UseMnemonic = Hint.UseMnemonic = Note.UseMnemonic = false;
            List.MouseUp += (s, e) => Open(e.Location);
            List.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Apps) { Open(new Point(8, (List.SelectedIndex + 1) * List.ItemHeight - List.TopPixel)); e.Handled = true; } };
            Body.Content.Controls.AddRange(new Control[] { List, Note, Hint, Title, CloseButton });
            Controls.Add(Body); Controls.Add(Line);
            Resize += (s, e) => LayoutContents();
            Body.Resize += (s, e) => LayoutContents();
            foreach (var label in new[] { Title, Note, Hint }) label.TextChanged += (s, e) => LayoutContents();
            Note.VisibleChanged += (s, e) => LayoutContents();
            CloseButton.Enter += (s, e) => Body.EnsureVisible(CloseButton);
            List.Enter += (s, e) => Body.EnsureVisible(List);
        }

        public IList<Participant> People { get { return _people; } }

        public static int ParagraphHeight(Label label, int width)
        {
            return Math.Max(label.Font.Height, TextRenderer.MeasureText(label.Text ?? "", label.Font,
                new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
        }

        private void LayoutContents()
        {
            if (_layingOut) return;
            _layingOut = true;
            try
            {
                int dpi = DisplayScale.Dpi(this);
                Func<int, int> px = value => DisplayScale.Pixels(value, dpi);
                int line = Math.Max(1, px(1));
                Line.SetBounds(0, 0, line, ClientSize.Height);
                Body.SetBounds(line, 0, Math.Max(0, ClientSize.Width - line), ClientSize.Height);
                int margin = px(10), gap = px(8);
                int width = Math.Max(1, Body.ContentWidth - margin * 2);
                int closeSide = Math.Max(px(30), CloseButton.Font.Height + px(8));
                int titleWidth = Math.Max(1, width - closeSide - gap);
                int headerHeight = Math.Max(closeSide, ParagraphHeight(Title, titleWidth));
                Title.SetBounds(margin, gap, titleWidth, headerHeight);
                CloseButton.SetBounds(margin + width - closeSide, gap + (headerHeight - closeSide) / 2, closeSide, closeSide);
                int y = Title.Bottom + gap;
                if (Note.Visible)
                {
                    Note.SetBounds(margin, y, width, ParagraphHeight(Note, width) + px(4));
                    y = Note.Bottom + gap;
                }
                int hintHeight = ParagraphHeight(Hint, width) + px(4);
                int contentHeight = Math.Max(Body.ClientSize.Height, y + Math.Max(px(48), List.ItemHeight * 2) + gap + hintHeight + margin);
                Body.SetContentHeight(contentHeight);
                Hint.SetBounds(margin, contentHeight - margin - hintHeight, width, hintHeight);
                List.SetBounds(margin, y, width, Math.Max(0, Hint.Top - gap - y));
            }
            finally { _layingOut = false; }
        }

        private void Open(Point at)
        {
            int i = List.SelectedIndex;
            if (i < 0 || i >= _people.Count) return;
            var h = PersonMenu; if (h != null) h(_people[i], at);
        }

        public void SetPeople(IList<Participant> people, Func<Participant, string> label, string title, string unreadable, bool stale = false)
        {
            var keep = List.SelectedIndex;
            _people = new List<Participant>(people);
            Stale = stale;
            List.Muted = stale;
            List.Items.Clear();
            foreach (var p in _people) List.Items.Add(label(p));
            List.EndUpdate();
            if (keep >= 0 && keep < _people.Count) List.SelectedIndex = keep;
            Title.Text = title;
            Note.Text = unreadable ?? "";
            Note.Visible = unreadable != null;
            if (_pal != null)
            {
                Title.ForeColor = stale ? _pal.Sub : _pal.Text;
                Note.ForeColor = stale ? _pal.Sub : _pal.Warning;
                List.ForeColor = stale ? _pal.Sub : _pal.Text;
            }
            LayoutContents();
        }

        public void ApplyStyle(Palette pal, Font font)
        {
            _pal = pal;
            BackColor = pal.Panel; Font = font;
            Body.Pal = new Palette { Back = pal.Panel, Panel = pal.Panel, Sub = pal.Sub };
            Title.BackColor = pal.Panel; Title.ForeColor = Stale ? pal.Sub : pal.Text;
            // WinForms keeps the old Font object when the new one is equal in value, so only a real change replaces (and frees) it
            var bold = new Font(font, FontStyle.Bold);
            if (_titleFont != null && _titleFont.Equals(bold)) bold.Dispose();
            else { var old = _titleFont; _titleFont = bold; Title.Font = bold; if (old != null && !ReferenceEquals(Title.Font, old)) old.Dispose(); }
            Hint.BackColor = pal.Panel; Hint.ForeColor = pal.Sub;
            Note.BackColor = pal.Panel; Note.ForeColor = Stale ? pal.Sub : pal.Warning;
            Line.BackColor = pal.Border;
            List.Pal = pal; List.Font = font; List.Muted = Stale; List.ForeColor = Stale ? pal.Sub : pal.Text;
            CloseButton.IconColor = pal.Text;
            LayoutContents();
        }
    }
}
