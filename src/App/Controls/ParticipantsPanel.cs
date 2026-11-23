using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
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
        private List<Participant> _people = new List<Participant>();
        private Font _titleFont;
        private Palette _pal;
        public bool Stale { get; private set; }
        public event Action<Participant, Point> PersonMenu;

        public ParticipantsPanel()
        {
            Width = PanelWidth;
            Visible = false;
            Padding = new Padding(1, 0, 0, 0);
            Title.AutoSize = false; Title.Height = 36; Title.TextAlign = ContentAlignment.MiddleLeft; Title.Padding = new Padding(10, 0, 0, 0); Title.Dock = DockStyle.Top;
            CloseButton.Size = new Size(32, 30);
            Hint.AutoSize = false; Hint.Dock = DockStyle.Bottom; Hint.Padding = new Padding(10, 6, 8, 8); Hint.Height = 52;
            Note.AutoSize = false; Note.Dock = DockStyle.Top; Note.Padding = new Padding(10, 2, 8, 2); Note.Visible = false; Note.Height = 22;
            List.Dock = DockStyle.Fill;
            Line.Dock = DockStyle.Left; Line.Width = 1;
            List.MouseUp += (s, e) => Open(e.Location);
            List.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Apps) { Open(new Point(8, (List.SelectedIndex + 1) * List.ItemHeight - List.TopPixel)); e.Handled = true; } };
            Controls.Add(List); Controls.Add(Note); Controls.Add(Hint); Controls.Add(Title); Controls.Add(Line);
            Controls.Add(CloseButton);
            CloseButton.BringToFront();
            Resize += (s, e) => CloseButton.Location = new Point(Width - CloseButton.Width - 4, 3);
        }

        public IList<Participant> People { get { return _people; } }

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
        }

        public void ApplyStyle(Palette pal, Font font)
        {
            _pal = pal;
            BackColor = pal.Panel; Font = font;
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
        }
    }
}
