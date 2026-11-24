using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Sending;

namespace NyangTalk.App.Rendering
{
    public enum RowKind { More, Date, Message, Emote, Meow, Outgoing }

    public enum PartKind { Avatar, Name, Time, Bubble, Text, Button, Center, Status, Image }

    public sealed class Part
    {
        public PartKind Kind;
        public Rectangle Rect;      // relative to the row's top-left
        public string Text;
        public string Action;       // for buttons: "more", "collapse", "retry", "delete", "resendConfirm", "resendCancel"
        public Color Fore, Back;
        public bool Bold, Small;
        /// <summary>Lines of a message body wrapped by <see cref="TextLayout"/>; null for other texts.</summary>
        public List<TextLine> Lines;
    }

    /// <summary>One laid-out line of the list. All rectangles are relative to the row; the row is <see cref="Height"/> tall.</summary>
    public sealed class Row
    {
        public RowKind Kind;
        public string Key;
        public TimelineItem Item;
        public OutgoingMessage Outgoing;
        public int Height;
        public bool Mine;
        public string SenderId;
        public readonly List<Part> Parts = new List<Part>();
        public string Tooltip;
        public string CopyText;
    }

    /// <summary>Text measuring is injected so layout can run in tests with the same GDI measurement the screen uses.</summary>
    public sealed class Measurer : IDisposable
    {
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

        public Font Font(RenderStyle s, bool bold, bool small)
        {
            var key = s.FontFamily + "|" + s.FontSize + "|" + bold + "|" + small;
            Font f;
            if (!_fonts.TryGetValue(key, out f))
            {
                f = NyangTalk.App.Platform.Fonts.Create(s.FontFamily, small ? Math.Max(7f, s.FontSize - 1.5f) : s.FontSize, bold ? FontStyle.Bold : FontStyle.Regular);
                _fonts[key] = f;
            }
            return f;
        }

        public Size Measure(string text, Font f, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            var sz = TextLayout.MeasureText(text, f, new Size(Math.Max(1, maxWidth), int.MaxValue), Flags);
            return new Size(Math.Min(sz.Width, maxWidth), sz.Height);
        }

        public static TextFormatFlags DrawFlags { get { return Flags; } }

        public void Dispose() { foreach (var f in _fonts.Values) f.Dispose(); _fonts.Clear(); }
    }

    /// <summary>Lays out timeline items into rows for a given width. Pure apart from text measurement.</summary>
    public sealed class ChatLayout
    {
        private readonly Measurer _m;
        public ChatLayout(Measurer m) { _m = m; }

        public Func<string, string> AliasOf = id => null;
        /// <summary>Request ids of "확인 못 함" messages whose "다시 보내기" was pressed once and now wait for "그래도 보내기".</summary>
        public Func<string, bool> AskingResend = id => false;
        /// <summary>Emote id → picture file and name from the game component (null: unknown).</summary>
        public Func<string, NyangTalk.Application.EmoteInfo> EmoteOf = id => null;
        public Func<string, bool> ImageExists = f => f != null && System.IO.File.Exists(f);
        public Func<string, string> Initial = name => DisplayName.Initial(name);
        public TimeZoneInfo TimeZone = TimeZoneInfo.Local;

        public string FormatTime(DateTime utc, RenderStyle s)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);
            return s.Clock24 ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        public Row Lay(TimelineItem item, int width, RenderStyle s)
        {
            var ds = item as DateSeparatorItem;
            if (ds != null) return Center(RowKind.Date, item.Key, item, "── " + ds.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · " + s.L("room.receivedOnly") + " ──", width, s, s.Theme.DatePill);
            var mg = item as MeowGroupItem;
            if (mg != null)
            {
                var r = Center(RowKind.Meow, item.Key, item, string.Format(s.L("meow.summary"), mg.TotalCount, mg.PeopleLabel), width, s, s.Theme.Soft);
                r.Tooltip = string.Join("\n", mg.TopPeople().Select(p => DisplayName.Format(p.SenderName, AliasOf(p.SenderId), s.NameMode) + " ×" + p.Count))
                    + "\n" + string.Format(s.L("meow.tooltipRange"), FormatTime(mg.FirstUtc, s), FormatTime(mg.LastUtc, s));
                r.CopyText = string.Format(s.L("meow.summary"), mg.TotalCount, mg.PeopleLabel);
                return r;
            }
            var mi = item as MessageItem;
            if (mi != null)
            {
                var m = mi.Message;
                return Message(RowKind.Message, item, m.SenderId, m.SenderName, m.IsMine, m.TimestampUtc, m.Body, mi.IsContinuation, width, s);
            }
            var eg = item as EmoteGroupItem;
            if (eg != null)
            {
                string body;
                if (eg.Mode == EmoteGroupMode.AllOnOneLine)
                    body = string.Join("  ", eg.Entries.Select(e => DisplayName.Format(e.SenderName, AliasOf(e.SenderId), s.NameMode) + " " + EmoteText(e, s)));
                else body = string.Join("  ", eg.Entries.Select(e => EmoteText(e, s)));
                var first = eg.Entries[0];
                bool multi = eg.Mode == EmoteGroupMode.AllOnOneLine;
                var content = s.EmoteAsImage ? EmoteContent(eg, multi, s) : null;
                var r = Message(RowKind.Emote, item, multi ? null : first.SenderId, multi ? "" : first.SenderName, !multi && eg.IsMine, eg.TimestampUtc, body, false, width, s, content);
                var names = string.Join(", ", eg.Entries.Select(e => EmoteName(e.EmoteId, s)).Distinct());
                r.Tooltip = multi ? names : DisplayName.Format(first.SenderName, AliasOf(first.SenderId), s.NameMode) + " · " + names;
                return r;
            }
            throw new ArgumentException("unknown item");
        }

        public string EmoteName(string emoteId, RenderStyle s)
        {
            var info = EmoteOf(emoteId);
            if (info != null && !string.IsNullOrEmpty(info.Name)) return info.Name;
            return string.Format(s.L("emote.name"), emoteId);
        }

        /// <summary>Pictures with ×N when the game component made them; null when no entry has a picture (text is used).</summary>
        private KeyValuePair<List<Part>, Size>? EmoteContent(EmoteGroupItem eg, bool multi, RenderStyle s)
        {
            if (!eg.Entries.Any(e => { var i = EmoteOf(e.EmoteId); return i != null && ImageExists(i.File); })) return null;
            var parts = new List<Part>();
            var f = _m.Font(s, false, false);
            int x = 0, h = 28;
            foreach (var e in eg.Entries)
            {
                if (x > 0) x += 8;
                if (multi)
                {
                    var n = DisplayName.Format(e.SenderName, AliasOf(e.SenderId), s.NameMode);
                    var ns = _m.Measure(n, f, 400);
                    parts.Add(new Part { Kind = PartKind.Text, Rect = new Rectangle(x, (h - ns.Height) / 2, ns.Width, ns.Height), Text = n });
                    x += ns.Width + 4;
                }
                var info = EmoteOf(e.EmoteId);
                if (info != null && ImageExists(info.File))
                {
                    parts.Add(new Part { Kind = PartKind.Image, Rect = new Rectangle(x, 0, 28, 28), Text = info.File });
                    x += 28;
                }
                else
                {
                    var t = EmoteName(e.EmoteId, s);
                    var ts = _m.Measure(t, f, 400);
                    parts.Add(new Part { Kind = PartKind.Text, Rect = new Rectangle(x, (h - ts.Height) / 2, ts.Width, ts.Height), Text = t });
                    x += ts.Width;
                }
                if (e.Count > 1)
                {
                    var c = "×" + e.Count;
                    var cs = _m.Measure(c, f, 200);
                    parts.Add(new Part { Kind = PartKind.Text, Rect = new Rectangle(x + 2, (h - cs.Height) / 2, cs.Width, cs.Height), Text = c });
                    x += cs.Width + 2;
                }
            }
            return new KeyValuePair<List<Part>, Size>(parts, new Size(x, h));
        }

        private string EmoteText(EmoteCount e, RenderStyle s)
        {
            // No game pictures are shipped; when the game component gives no picture the emote name is shown (50-SPEC 2장).
            return EmoteName(e.EmoteId, s) + (e.Count > 1 ? " ×" + e.Count : "");
        }

        public Row MoreRow(bool canMore, bool canCollapse, int width, RenderStyle s)
        {
            var r = new Row { Kind = RowKind.More, Key = "more" };
            var f = _m.Font(s, false, true);
            int x = s.Gutter;
            int h = 0;
            foreach (var b in new[] { canMore ? "history.more" : null, canCollapse ? "history.collapse" : null })
            {
                if (b == null) continue;
                var text = s.L(b);
                var sz = _m.Measure(text, f, width - 2 * s.Gutter);
                var rect = new Rectangle(x, 6, sz.Width + 16, sz.Height + 8);
                r.Parts.Add(new Part { Kind = PartKind.Button, Rect = rect, Text = text, Action = b == "history.more" ? "more" : "collapse", Fore = RenderStyle.C(s.Theme.Text), Back = RenderStyle.C(s.Theme.Panel), Small = true });
                x = rect.Right + 8;
                h = Math.Max(h, rect.Bottom + 6);
            }
            r.Height = h;
            return r;
        }

        public Row OutgoingRow(OutgoingMessage o, int width, RenderStyle s)
        {
            var r = Message(RowKind.Outgoing, null, null, "", true, o.CreatedUtc, o.Text, false, width, s);
            r.Key = "out:" + o.RequestId;
            r.Outgoing = o;
            string status;
            switch (o.State)
            {
                case OutgoingState.Sending: status = s.L("send.sending"); break;
                case OutgoingState.Delivered: status = s.L("send.delivered"); break;
                case OutgoingState.Unconfirmed: status = s.L("send.unconfirmed"); break;
                case OutgoingState.Rejected: status = string.Format(s.L("send.rejected"), o.Reason == "speech-not-ready" ? s.L("send.speechNotReady") : o.Reason ?? ""); break;
                case OutgoingState.NotDelivered: status = s.L("send.notDelivered") + " · " + s.L("send.filtered") + (o.PunctuationHint ? " " + s.L("send.filteredPunct") : ""); break;
                default: status = s.L("send.failed"); break;
            }
            var f = _m.Font(s, false, true);
            var bubble = r.Parts.First(p => p.Kind == PartKind.Bubble).Rect;
            var sz = _m.Measure(status, f, width - 2 * s.Gutter);
            int y = r.Height;
            var statusRect = new Rectangle(Math.Max(s.Gutter, bubble.Right - sz.Width), y, sz.Width, sz.Height);
            r.Parts.Add(new Part { Kind = PartKind.Status, Rect = statusRect, Text = status, Fore = RenderStyle.C(s.Theme.SubText), Small = true });
            y = statusRect.Bottom + 2;
            if (o.NeedsAction)
            {
                // 95-SPEC 2.3: "확인 못 함" asks once more right under the message (no pop-up) before sending again
                bool asking = o.State == OutgoingState.Unconfirmed && AskingResend(o.RequestId);
                string[] buttons = asking ? new[] { "send.resendCancel", "send.resendAnyway" } : new[] { "send.delete", "send.retry" };
                if (asking)
                {
                    var ask = s.L("send.resendAsk");
                    var qs = _m.Measure(ask, f, width - 2 * s.Gutter);
                    r.Parts.Add(new Part { Kind = PartKind.Status, Rect = new Rectangle(Math.Max(s.Gutter, bubble.Right - qs.Width), y, qs.Width, qs.Height), Text = ask, Fore = RenderStyle.C(s.Theme.Text), Small = true });
                    y += qs.Height + 2;
                }
                int x = bubble.Right;
                foreach (var b in buttons)
                {
                    var text = s.L(b);
                    var bs = _m.Measure(text, f, width);
                    x -= bs.Width + 14;
                    r.Parts.Add(new Part { Kind = PartKind.Button, Rect = new Rectangle(Math.Max(s.Gutter, x), y, bs.Width + 12, bs.Height + 6), Text = text, Action = ActionOf(b), Fore = RenderStyle.C(s.Theme.AccentText), Back = RenderStyle.C(s.Theme.Accent), Small = true });
                    x -= 6;
                }
                y += f.Height + 8;
            }
            r.Height = y + s.Pad;
            return r;
        }

        private static string ActionOf(string key)
        {
            switch (key)
            {
                case "send.retry": return "retry";
                case "send.resendAnyway": return "resendConfirm";
                case "send.resendCancel": return "resendCancel";
                default: return "delete";
            }
        }

        private Row Center(RowKind kind, string key, TimelineItem item, string text, int width, RenderStyle s, NyangTalk.Domain.People.Rgb? back)
        {
            var f = _m.Font(s, false, true);
            int maxW = Math.Max(40, width - 2 * s.Gutter - 16);
            var sz = _m.Measure(text, f, maxW);
            var r = new Row { Kind = kind, Key = key, Item = item, CopyText = text };
            int w = sz.Width + 16;
            var rect = new Rectangle((width - w) / 2, s.Pad + 2, w, sz.Height + 6);
            r.Parts.Add(new Part { Kind = PartKind.Center, Rect = rect, Text = text, Fore = RenderStyle.C(kind == RowKind.Meow ? s.Theme.MidText : s.Theme.Notice), Back = back.HasValue ? RenderStyle.C(back.Value) : Color.Empty, Small = true });
            r.Height = rect.Bottom + s.Pad + 2;
            return r;
        }

        /// <summary>Colour of a sender name and bubble background for a person (60-SPEC 3장).</summary>
        public void PersonPaint(RenderStyle s, string senderId, bool mine, out Color nameColor, out Color bubbleBack, out Color bubbleText, Rgb nameBackground)
        {
            var th = s.Theme;
            var baseColor = s.Colors.BaseColor(senderId, mine);
            var bubble = mine ? th.MineBubble : th.OtherBubble;
            var text = mine ? th.MineText : th.OtherText;
            if (s.OutlineOnly) { bubble = th.Window; text = th.Text; }
            if (baseColor.HasValue && s.Colors.ColorsBackground) bubble = ColorMath.Tint(baseColor.Value, bubble, text);
            nameColor = RenderStyle.C(th.SubText);
            if (baseColor.HasValue && s.Colors.ColorsName) nameColor = RenderStyle.C(ColorMath.EnsureTextContrast(baseColor.Value, nameBackground));
            else nameColor = RenderStyle.C(th.Text);
            bubbleBack = RenderStyle.C(bubble);
            bubbleText = RenderStyle.C(text);
        }

        private Row Message(RowKind kind, TimelineItem item, string senderId, string senderName, bool mine, DateTime utc, string body, bool continuation, int width, RenderStyle s, KeyValuePair<List<Part>, Size>? content = null)
        {
            var r = new Row { Kind = kind, Key = item == null ? null : item.Key, Item = item, Mine = mine, SenderId = senderId, CopyText = body };
            var f = _m.Font(s, false, false);
            var fs = _m.Font(s, false, true);
            var fb = _m.Font(s, true, true);
            int pad = s.Pad, g = s.Gutter;
            bool showAvatar = s.AvatarSize > 0 && (!mine || s.ShowMyCat);
            bool avatarRight = mine && s.MyCatRight;
            int av = showAvatar ? s.AvatarSize : 0;
            bool hideHead = continuation;
            bool drawAvatar = showAvatar && !(continuation && s.CatFirstOnly) && !(continuation && s.Shape != ChatShape.Compact && false);
            if (continuation && s.CatFirstOnly) drawAvatar = false;
            // mine without avatar still aligns right; others always left
            string name = mine ? "" : DisplayName.Format(senderName, AliasOf(senderId), s.NameMode);
            string time = FormatTime(utc, s);
            Color nameColor, back, fore;
            PersonPaint(s, senderId, mine, out nameColor, out back, out fore, s.NameInsideBubble ? (mine ? s.Theme.MineBubble : s.Theme.OtherBubble) : s.Theme.Window);
            if (s.Colors.ColorsBackground && !mine && s.NameInsideBubble)
            {
                // name sits on the tinted bubble; re-check contrast against it
                var tinted = new NyangTalk.Domain.People.Rgb(back.R, back.G, back.B);
                var bc = s.Colors.BaseColor(senderId, mine);
                if (bc.HasValue && s.Colors.ColorsName) nameColor = RenderStyle.C(ColorMath.EnsureTextContrast(bc.Value, tinted));
            }

            int avatarCol = av > 0 ? av + 8 : 0;
            int contentLeft = g + (avatarRight ? 0 : avatarCol);
            int contentRight = width - g - (avatarRight ? avatarCol : 0);
            int contentWidth = Math.Max(60, contentRight - contentLeft);

            if (s.Shape == ChatShape.Compact)
            {
                // "HH:mm name: text" on one flowing line, small avatar on the left
                var head = time + "  " + (mine ? "" : name + ": ");
                // the name sits on the tinted band when background colouring is on
                var bc0 = s.Colors.BaseColor(senderId, mine);
                if (!mine && bc0.HasValue && s.Colors.ColorsName)
                    nameColor = RenderStyle.C(ColorMath.EnsureTextContrast(bc0.Value, s.Colors.ColorsBackground ? new NyangTalk.Domain.People.Rgb(back.R, back.G, back.B) : s.Theme.Window));
                var hs = _m.Measure(head, fb, contentWidth);
                int textLeft = contentLeft + hs.Width;
                int textWidth = Math.Max(40, contentRight - textLeft);
                var compactLines = content.HasValue ? null : TextLayout.Wrap(body, f, textWidth);
                var ts = content.HasValue ? content.Value.Value : TextLayout.Measure(compactLines, f);
                int lineH = Math.Max(hs.Height, ts.Height);
                int top = pad;
                if (s.Colors.ColorsBackground && !mine)
                    r.Parts.Add(new Part { Kind = PartKind.Bubble, Rect = new Rectangle(g / 2, 0, width - g, lineH + 2 * pad), Back = back });
                else if (mine)
                    r.Parts.Add(new Part { Kind = PartKind.Bubble, Rect = new Rectangle(g / 2, 0, width - g, lineH + 2 * pad), Back = RenderStyle.C(s.Theme.MineBubble) });
                if (drawAvatar) r.Parts.Add(new Part { Kind = PartKind.Avatar, Rect = new Rectangle(avatarRight ? contentRight + 8 : g, top, av, av), Text = Initial(mine ? "me" : senderName) });
                r.Parts.Add(new Part { Kind = PartKind.Name, Rect = new Rectangle(contentLeft, top, hs.Width, hs.Height), Text = head, Fore = mine ? RenderStyle.C(s.Theme.MineText) : nameColor, Bold = true, Small = true });
                if (content.HasValue) AddContent(r, content.Value.Key, textLeft, top, mine ? RenderStyle.C(s.Theme.MineText) : fore);
                else r.Parts.Add(new Part { Kind = PartKind.Text, Rect = new Rectangle(textLeft, top, ts.Width, ts.Height), Text = body, Fore = mine ? RenderStyle.C(s.Theme.MineText) : fore, Lines = compactLines });
                r.Height = Math.Max(lineH, drawAvatar ? av : 0) + 2 * pad;
                return r;
            }

            int y = pad;
            Size nameSize = Size.Empty, timeSize = _m.Measure(time, fs, contentWidth);
            bool timeBeside = mine && !s.NameInsideBubble;
            int maxBubble = Math.Max(60, (int)(contentWidth * (s.Shape == ChatShape.Card ? 0.92 : 0.78)));
            if (timeBeside && !hideHead) maxBubble = Math.Min(maxBubble, Math.Max(40, contentWidth - timeSize.Width - 6));
            int bubblePadX = s.Shape == ChatShape.Card ? 10 : 9, bubblePadY = s.Shape == ChatShape.Card ? 7 : 6;
            int innerMax = maxBubble - 2 * bubblePadX;
            var bodyLines = content.HasValue ? null : TextLayout.Wrap(body, f, innerMax);
            var textSize = content.HasValue ? content.Value.Value : TextLayout.Measure(bodyLines, f);
            int nameH = 0;
            if (!hideHead && !s.NameInsideBubble && !mine)
            {
                // header line above the bubble: name + time
                nameSize = name.Length > 0 ? _m.Measure(name, fb, Math.Max(20, contentWidth - timeSize.Width - 8)) : Size.Empty;
                nameH = Math.Max(nameSize.Height, timeSize.Height);
                if (name.Length > 0) r.Parts.Add(new Part { Kind = PartKind.Name, Rect = new Rectangle(contentLeft, y, nameSize.Width, nameSize.Height), Text = name, Fore = nameColor, Bold = true, Small = true });
                r.Parts.Add(new Part { Kind = PartKind.Time, Rect = new Rectangle(contentLeft + (nameSize.Width > 0 ? nameSize.Width + 8 : 0), y, timeSize.Width, timeSize.Height), Text = time, Fore = RenderStyle.C(s.Theme.SubText), Small = true });
                y += nameH + 2;
            }

            int innerW = textSize.Width;
            int innerH = textSize.Height;
            Size inName = Size.Empty;
            if (s.NameInsideBubble && !hideHead)
            {
                var head = mine ? time : name + "  " + time;
                inName = _m.Measure(head, fb, innerMax);
                innerW = Math.Max(innerW, inName.Width);
                innerH += inName.Height + 2;
            }
            if (s.Shape == ChatShape.Card) innerW = Math.Max(innerW, Math.Min(innerMax, 160));
            int bw = innerW + 2 * bubblePadX, bh = innerH + 2 * bubblePadY;
            int bx = mine ? contentRight - bw : contentLeft;
            var bubbleRect = new Rectangle(bx, y, bw, bh);
            r.Parts.Add(new Part { Kind = PartKind.Bubble, Rect = bubbleRect, Back = back });
            if (timeBeside && !hideHead)
                r.Parts.Add(new Part { Kind = PartKind.Time,
                    Rect = new Rectangle(bx - 6 - timeSize.Width, Math.Max(y, bubbleRect.Bottom - timeSize.Height), timeSize.Width, timeSize.Height),
                    Text = time, Fore = RenderStyle.C(s.Theme.SubText), Small = true });
            int ty = y + bubblePadY;
            if (inName.Width > 0)
            {
                r.Parts.Add(new Part { Kind = PartKind.Name, Rect = new Rectangle(bx + bubblePadX, ty, inName.Width, inName.Height), Text = mine ? time : name + "  " + time, Fore = mine ? fore : nameColor, Bold = true, Small = true });
                ty += inName.Height + 2;
            }
            if (content.HasValue) AddContent(r, content.Value.Key, bx + bubblePadX, ty, fore);
            else r.Parts.Add(new Part { Kind = PartKind.Text, Rect = new Rectangle(bx + bubblePadX, ty, textSize.Width, textSize.Height), Text = body, Fore = fore, Lines = bodyLines });
            if (drawAvatar)
            {
                int ax = avatarRight ? width - g - av : g;
                int avatarY = mine ? bubbleRect.Y : pad;
                r.Parts.Add(new Part { Kind = PartKind.Avatar, Rect = new Rectangle(ax, avatarY, av, av), Text = Initial(mine ? (senderName ?? "me") : senderName) });
            }
            r.Height = Math.Max(bubbleRect.Bottom, drawAvatar ? pad + av : 0) + pad;
            return r;
        }

        private static void AddContent(Row r, List<Part> parts, int left, int top, Color fore)
        {
            foreach (var p in parts)
            {
                var rect = p.Rect; rect.Offset(left, top);
                r.Parts.Add(new Part { Kind = p.Kind, Rect = rect, Text = p.Text, Fore = fore });
            }
        }
    }
}
