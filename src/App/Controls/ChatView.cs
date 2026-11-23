using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Sending;

namespace NyangTalk.App.Controls
{
    /// <summary>
    /// Virtualised chat list (50-SPEC 5장): every row has an exact measured height so the scroll bar is exact, but only
    /// visible rows are painted. Keeps the reading position when content changes unless the view is at the bottom.
    /// </summary>
    public sealed class ChatView : Control
    {
        private readonly ThemedScrollBar _bar = new ThemedScrollBar();   // theme colours, never the system grey (90-SPEC 1.6)
        private readonly Button _newButton = new Button();
        private readonly ToolTip _tip = new ToolTip();
        private readonly Measurer _measurer = new Measurer();
        private readonly ChatLayout _layout;
        private readonly Dictionary<string, Row> _cache = new Dictionary<string, Row>();
        private List<Row> _rows = new List<Row>();
        private int[] _tops = new int[0];
        private int _total;
        private int _scroll;
        private int _layoutWidth = -1;
        private int _styleVersion;
        private int _newCount;
        private string _selectedKey;
        private string _highlightKey;
        private string _tipKey;
        private RenderStyle _style = new RenderStyle();

        public Func<string, Image> Portrait;
        /// <summary>Picture by file path (emote pictures).</summary>
        public Func<string, Image> Picture;
        public event Action<string, Row> ActionClicked;
        public event Action<Row, Point> PersonMenu;
        public event Action<bool> BottomChanged;
        public int PaintedRowsLastPaint { get; private set; }

        public ChatView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _layout = new ChatLayout(_measurer);
            _bar.Dock = DockStyle.Right;
            _bar.ValueChanged += (s, e) => { if (_bar.Value != _scroll) SetScroll(_bar.Value); };
            Controls.Add(_bar);
            _newButton.Visible = false;
            _newButton.AutoSize = true;
            _newButton.FlatStyle = FlatStyle.Flat;
            _newButton.Click += (s, e) => ScrollToBottom();
            Controls.Add(_newButton);
            TabStop = true;
            AccessibleName = "chat";
        }

        public ChatLayout Layout2 { get { return _layout; } }
        public IList<Row> Rows { get { return _rows; } }
        public int TotalHeight { get { return _total; } }
        public ThemedScrollBar Bar { get { return _bar; } }
        /// <summary>Previews hide the scroll bar and show the newest rows.</summary>
        public bool ScrollBarVisible { get { return _bar.Visible; } set { _bar.Visible = value; } }
        public int ScrollOffset { get { return _scroll; } }
        public int ViewportHeight { get { return Math.Max(1, ClientSize.Height); } }
        public int NewMessageCount { get { return _newCount; } }
        public bool NewButtonVisible { get { return _newButton.Visible; } }
        public string SelectedKey { get { return _selectedKey; } set { _selectedKey = value; Invalidate(); } }
        public bool IsAtBottom { get { return _scroll >= MaxScroll - 2; } }
        private int MaxScroll { get { return Math.Max(0, _total - ViewportHeight); } }
        private int ContentWidth { get { return Math.Max(100, ClientSize.Width - (_bar.Visible ? _bar.Width : 0)); } }

        public RenderStyle Style
        {
            get { return _style; }
            set { _style = value; _styleVersion++; _cache.Clear(); BackColor = RenderStyle.C(value.Theme.Window); _bar.Pal = Palette.From(value.Theme); _newButton.BackColor = RenderStyle.C(value.Theme.Accent); _newButton.ForeColor = RenderStyle.C(value.Theme.AccentText); Relayout(true); }
        }

        private List<TimelineItem> _items = new List<TimelineItem>();
        private IList<OutgoingMessage> _outgoing = new List<OutgoingMessage>();
        private bool _canMore, _canCollapse;

        public void SetContent(List<TimelineItem> items, IList<OutgoingMessage> outgoing, bool canMore, bool canCollapse)
        {
            bool wasBottom = _stickBottom || IsAtBottom || _rows.Count == 0;
            string anchorKey = null; int anchorDelta = 0;
            if (!wasBottom) FindAnchor(out anchorKey, out anchorDelta);
            var oldKeys = new HashSet<string>(_rows.Where(r => r.Key != null).Select(r => r.Key));
            _items = items; _outgoing = outgoing.ToList(); _canMore = canMore; _canCollapse = canCollapse;
            Relayout(false);
            if (wasBottom) { _newCount = 0; ScrollToBottomInternal(); }
            else
            {
                int added = _rows.Count(r => (r.Kind == RowKind.Message || r.Kind == RowKind.Emote) && r.Key != null && !oldKeys.Contains(r.Key) && IsAfterAnchor(r, anchorKey));
                _newCount += added;
                RestoreAnchor(anchorKey, anchorDelta);
            }
            UpdateNewButton();
            Invalidate();
        }

        private bool IsAfterAnchor(Row r, string anchorKey)
        {
            if (anchorKey == null) return true;
            int ia = _rows.FindIndex(x => x.Key == anchorKey), ir = _rows.IndexOf(r);
            return ia < 0 || ir > ia;
        }

        private void FindAnchor(out string key, out int delta)
        {
            key = null; delta = 0;
            int i = RowAt(_scroll);
            if (i < 0 || i >= _rows.Count) return;
            for (int j = i; j < _rows.Count; j++) if (_rows[j].Key != null) { key = _rows[j].Key; delta = _tops[j] - _scroll; return; }
        }

        private void RestoreAnchor(string key, int delta)
        {
            int i = key == null ? -1 : _rows.FindIndex(r => r.Key == key);
            if (i >= 0) SetScroll(_tops[i] - delta, false);
            else SetScroll(_scroll, false);
        }

        private void Relayout(bool keepPosition)
        {
            string anchorKey = null; int anchorDelta = 0;
            bool bottom = _stickBottom || IsAtBottom;
            if (keepPosition && !bottom) FindAnchor(out anchorKey, out anchorDelta);
            int w = ContentWidth;
            if (w != _layoutWidth) { _cache.Clear(); _layoutWidth = w; }
            var rows = new List<Row>(_items.Count + 4);
            if (_canMore || _canCollapse) rows.Add(_layout.MoreRow(_canMore, _canCollapse, w, _style));
            foreach (var it in _items)
            {
                Row r;
                var ck = it.Key + "|" + CacheSalt(it);
                if (!_cache.TryGetValue(ck, out r)) { r = _layout.Lay(it, w, _style); _cache[ck] = r; }
                rows.Add(r);
            }
            foreach (var o in _outgoing) rows.Add(_layout.OutgoingRow(o, w, _style));
            _rows = rows;
            _tops = new int[rows.Count];
            int y = 0;
            for (int i = 0; i < rows.Count; i++) { _tops[i] = y; y += rows[i].Height; }
            _total = y;
            if (keepPosition)
            {
                if (bottom) ScrollToBottomInternal(); else RestoreAnchor(anchorKey, anchorDelta);
            }
            UpdateBar();
            Invalidate();
        }

        private string CacheSalt(TimelineItem it)
        {
            var eg = it as EmoteGroupItem; if (eg != null) return eg.TotalCount + ":" + EmoteSalt(eg);
            var mg = it as MeowGroupItem; if (mg != null) return mg.TotalCount + ":" + mg.PeopleLabel;
            var mi = it as MessageItem; if (mi != null) return mi.IsContinuation ? "c" : "h";
            return "";
        }

        /// <summary>Changes when a picture or name for one of the group's emotes arrives, so the row is laid out again.</summary>
        public Func<EmoteGroupItem, string> EmoteSalt = eg => "";

        /// <summary>Call when aliases/colours change.</summary>
        public void InvalidateLayout() { _cache.Clear(); Relayout(true); }

        private bool _stickBottom = true;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_layoutWidth != ContentWidth) Relayout(true);
            else if (_stickBottom) ScrollToBottomInternal();
            else UpdateBar();
            UpdateNewButton();
        }

        private void UpdateBar()
        {
            _bar.Minimum = 0;
            _bar.Maximum = Math.Max(0, _total - 1);
            _bar.LargeChange = Math.Max(1, ViewportHeight);
            _bar.SmallChange = 40;
            _bar.Enabled = _total > ViewportHeight;
            int v = Math.Min(_scroll, Math.Max(0, _bar.Maximum - _bar.LargeChange + 1));
            if (_bar.Value != v) _bar.Value = Math.Max(0, v);
        }

        private void SetScroll(int value, bool fromUser = true)
        {
            int nv = Math.Max(0, Math.Min(MaxScroll, value));
            bool wasBottom = IsAtBottom;
            _scroll = nv;
            _stickBottom = IsAtBottom;
            UpdateBar();
            if (IsAtBottom) { _newCount = 0; UpdateNewButton(); }
            if (wasBottom != IsAtBottom || fromUser) { var h = BottomChanged; if (h != null) h(IsAtBottom); }
            Invalidate();
        }

        private void ScrollToBottomInternal() { _scroll = MaxScroll; _stickBottom = true; UpdateBar(); }

        public void ScrollToBottom()
        {
            SetScroll(MaxScroll);
            _newCount = 0;
            UpdateNewButton();
        }

        /// <summary>Jump to an item (search result) even if it has never been painted; highlights it.</summary>
        public void ScrollToItemKey(string key)
        {
            int i = _rows.FindIndex(r => r.Key == key);
            if (i < 0) return;
            _highlightKey = key;
            SetScroll(_tops[i] - ViewportHeight / 3);
        }

        public void ClearHighlight() { _highlightKey = null; Invalidate(); }

        public int RowTop(int index) { return _tops[index]; }

        private void UpdateNewButton()
        {
            bool show = _newCount > 0 && !IsAtBottom;
            if (show)
            {
                _newButton.Text = string.Format(_style.L("history.newMessages"), _newCount);
                _newButton.AccessibleName = _newButton.Text;
                var sz = _newButton.GetPreferredSize(Size.Empty);
                _newButton.Location = new Point(Math.Max(0, (ContentWidth - sz.Width) / 2), Math.Max(0, ClientSize.Height - sz.Height - 10));
                _newButton.BringToFront();
            }
            _newButton.Visible = show;
        }

        public int RowAt(int y)
        {
            if (_tops.Length == 0) return -1;
            int lo = 0, hi = _tops.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_tops[mid] <= y) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(RenderStyle.C(_style.Theme.Window));
            int w = ContentWidth;
            int first = RowAt(_scroll);
            int painted = 0;
            if (first >= 0)
            {
                for (int i = first; i < _rows.Count && _tops[i] < _scroll + ViewportHeight; i++)
                {
                    var r = _rows[i];
                    ChatPainter.Paint(g, r, _tops[i] - _scroll, w, _style, _measurer, Portrait, r.Key != null && r.Key == _selectedKey && _selStart < 0, r.Key != null && r.Key == _highlightKey, Picture,
                        r.Key != null && r.Key == _selKey ? Math.Min(_selStart, _selEnd) : -1, r.Key != null && r.Key == _selKey ? Math.Max(_selStart, _selEnd) : -1);
                    painted++;
                }
            }
            PaintedRowsLastPaint = painted;
        }

        /// <summary>Renders the whole visible viewport into a bitmap (screen checks, 01-OVERVIEW 6장).</summary>
        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using (var g = Graphics.FromImage(bmp)) OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, bmp.Size)));
            return bmp;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetScroll(_scroll - e.Delta * SystemInformation.MouseWheelScrollLines * 40 / 120 / 3 * 2);
        }

        private Row HitRow(Point p, out Part part)
        {
            part = null;
            int i = RowAt(p.Y + _scroll);
            if (i < 0 || i >= _rows.Count) return null;
            var r = _rows[i];
            var local = new Point(p.X, p.Y + _scroll - _tops[i]);
            if (local.Y < 0 || local.Y >= r.Height) return null;
            foreach (var pt in r.Parts) if (pt.Rect.Contains(local) && (pt.Kind == PartKind.Button || pt.Kind == PartKind.Avatar || pt.Kind == PartKind.Name)) { part = pt; break; }
            if (part == null) foreach (var pt in r.Parts) if (pt.Rect.Contains(local)) { part = pt; break; }
            return r;
        }

        // ---- partial text selection (50-SPEC 1장): one message body at a time, kept by row key ----
        private string _selKey;
        private int _selStart = -1, _selEnd = -1;
        private bool _dragging;
        private int _clicks;
        private DateTime _lastClick;
        private Point _lastClickAt;

        public string SelectionKey { get { return _selKey; } }
        public bool HasTextSelection { get { return _selKey != null && _selStart >= 0 && _selEnd != _selStart; } }

        public void ClearTextSelection() { _selKey = null; _selStart = _selEnd = -1; _dragging = false; Invalidate(); }

        private Part BodyPart(Row r) { return r == null ? null : r.Parts.FirstOrDefault(p => p.Kind == PartKind.Text && p.Lines != null); }

        private int CharAt(Row r, Part body, Point viewPoint)
        {
            int i = _rows.IndexOf(r);
            var local = new Point(viewPoint.X - body.Rect.X, viewPoint.Y + _scroll - _tops[i] - body.Rect.Y);
            return TextLayout.IndexAt(body.Text, body.Lines, local, _measurer.Font(_style, body.Bold, body.Small));
        }

        /// <summary>Mouse input entry point (also used by the screen checks).</summary>
        public void PointerDown(Point p, MouseButtons button)
        {
            Focus();
            Part part;
            var row = HitRow(p, out part);
            if (button == MouseButtons.Left)
            {
                bool again = (DateTime.UtcNow - _lastClick).TotalMilliseconds <= SystemInformation.DoubleClickTime
                    && Math.Abs(p.X - _lastClickAt.X) <= 4 && Math.Abs(p.Y - _lastClickAt.Y) <= 4;
                _clicks = again ? _clicks + 1 : 1;
                _lastClick = DateTime.UtcNow; _lastClickAt = p;
            }
            if (row == null) { ClearTextSelection(); return; }
            if (button == MouseButtons.Left && part != null && part.Kind == PartKind.Button)
            {
                var h = ActionClicked; if (h != null) h(part.Action, row);
                return;
            }
            var body = BodyPart(row);
            if (button == MouseButtons.Left)
            {
                int ri = _rows.IndexOf(row);
                var local = new Point(p.X, p.Y + _scroll - _tops[ri]);
                var hitRect = body == null ? Rectangle.Empty : Rectangle.Inflate(body.Rect, 2, 2);
                if (body != null && hitRect.Contains(local))
                {
                    int idx = CharAt(row, body, p);
                    _selKey = row.Key;
                    if (_clicks >= 3) { _selStart = 0; _selEnd = body.Text.Length; _dragging = false; }
                    else if (_clicks == 2) { int a, b; TextLayout.WordAt(body.Text, idx, out a, out b); _selStart = a; _selEnd = b; _dragging = false; }
                    else { _selStart = _selEnd = idx; _dragging = true; }
                }
                else ClearTextSelection(); // clicking elsewhere releases the selection
            }
            _selectedKey = row.Key;
            Invalidate();
            if (button == MouseButtons.Right || (part != null && (part.Kind == PartKind.Avatar || part.Kind == PartKind.Name) && button == MouseButtons.Left))
            {
                var h = PersonMenu; if (h != null) h(row, p);
            }
        }

        public void PointerMove(Point p)
        {
            if (!_dragging || _selKey == null) return;
            var row = _rows.FirstOrDefault(r => r.Key == _selKey);
            var body = BodyPart(row);
            if (body == null) return;
            _selEnd = CharAt(row, body, p);   // stays inside this one message
            Invalidate();
        }

        public void PointerUp() { _dragging = false; }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); PointerUp(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointerDown(e.Location, e.Button);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button == MouseButtons.Left) PointerMove(e.Location);
            Part part;
            var row = HitRow(e.Location, out part);
            var key = row == null || row.Tooltip == null ? null : row.Key;
            if (key == _tipKey) return;
            _tipKey = key;
            if (key == null) _tip.Hide(this); else _tip.Show(row.Tooltip, this, e.X + 12, e.Y + 16, 8000);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode) { case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true; }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.PageUp: SetScroll(_scroll - ViewportHeight + 40); break;
                case Keys.PageDown: SetScroll(_scroll + ViewportHeight - 40); break;
                case Keys.Up: SetScroll(_scroll - 40); break;
                case Keys.Down: SetScroll(_scroll + 40); break;
                case Keys.Home: if (e.Control) SetScroll(0); break;
                case Keys.End: if (e.Control) ScrollToBottom(); break;
                case Keys.C: if (e.Control) CopySelected(); break;
                case Keys.Escape: ClearTextSelection(); break;
            }
        }

        /// <summary>What Ctrl+C / "복사" copies: the chosen part of the body, or the whole message when nothing is chosen.</summary>
        public string SelectedText
        {
            get
            {
                if (HasTextSelection)
                {
                    var sr = _rows.FirstOrDefault(x => x.Key == _selKey);
                    var body = BodyPart(sr);
                    if (body != null)
                    {
                        int a = Math.Max(0, Math.Min(_selStart, _selEnd)), b = Math.Min(body.Text.Length, Math.Max(_selStart, _selEnd));
                        return body.Text.Substring(a, b - a);
                    }
                }
                var r = _rows.FirstOrDefault(x => x.Key != null && x.Key == _selectedKey);
                return r == null ? null : r.CopyText;
            }
        }

        public void CopySelected()
        {
            var t = SelectedText;
            if (!string.IsNullOrEmpty(t)) { try { Clipboard.SetText(t); } catch (System.Runtime.InteropServices.ExternalException) { } }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _measurer.Dispose(); _tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
