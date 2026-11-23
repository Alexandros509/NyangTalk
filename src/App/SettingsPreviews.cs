using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Sending;
using NyangTalk.Domain.People;
using NyangTalk.App.Platform;

namespace NyangTalk.App
{
    /// <summary>
    /// Invented sample conversations for the settings previews (90-SPEC 1.4). Names and sentences come from the
    /// translation table, so they follow the app language ("보리", "콩이" in Korean).
    /// </summary>
    public static class Previews
    {
        private const string A = "preview-a", B = "preview-b", Me = "preview-me";
        private static DateTime SampleTime { get { return DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc).AddHours(13).AddMinutes(35); } }

        public static void Initialize(ChatView view, SettingsForm form)
        {
            var pictures = new SamplePictures();
            view.Portrait = id => pictures.Portrait(id, form.Prefs.Settings.Get("char.scope") == "face");
            view.Picture = pictures.Emote;
            view.Layout2.ImageExists = file => pictures.Emote(file) != null;
            view.Disposed += (s, e) => pictures.Dispose();
        }

        private static ChatMessage Msg(SettingsForm f, string id, string sender, string nameKey, string bodyKey, DateTime t, bool mine = false)
        {
            return new ChatMessage { Id = id, SenderId = sender, SenderName = f.L(nameKey), Body = f.L(bodyKey), TimestampUtc = t, IsMine = mine };
        }

        private static void Show(ChatView v, RenderStyle style, List<ChatMessage> ms, TimelineOptions opt, SettingsForm f)
        {
            v.Style = style;
            v.Layout2.AliasOf = id => id == A ? f.L("preview.aliasA") : null;
            v.Layout2.EmoteOf = id => new EmoteInfo { Name = f.L("preview.emote." + id), File = "sample-" + id };
            v.SetContent(TimelineBuilder.Build(ms, opt), new List<OutgoingMessage>(), false, false);
        }

        private static RenderStyle Style(SettingsForm f)
        {
            var colors = new PersonColors { Mode = f.Prefs.Colors.Mode, Placement = f.Prefs.Colors.Placement };
            // Demonstrate manual choices on sample people without writing sample IDs into the user's preferences.
            if (colors.Mode == PersonColorMode.Manual)
            {
                colors.Manual[A] = (PersonColors.AutoIndex(A) + 1) % PersonColors.Palette.Length;
                colors.Manual[B] = (PersonColors.AutoIndex(B) + 2) % PersonColors.Palette.Length;
            }
            return RenderStyle.From(f.Prefs.Settings, colors, f.Prefs.NameMode);
        }

        /// <summary>Normal conversation: other people, a consecutive message, my message.</summary>
        public static void Conversation(SettingsForm f, ChatView v, bool people)
        {
            var t = SampleTime;
            var ms = new List<ChatMessage>
            {
                Msg(f, "p1", A, "preview.nameA", "preview.line1", t),
                Msg(f, "p2", B, "preview.nameB", "preview.line2", t.AddSeconds(30)),
                Msg(f, "p3", B, "preview.nameB", "preview.line3", t.AddSeconds(40)),
                Msg(f, "p4", Me, "preview.me", "preview.line4", t.AddSeconds(70), true),
            };
            if (people) ms.Add(Msg(f, "p5", A, "preview.nameA", "preview.line5", t.AddSeconds(90)));
            Show(v, Style(f), ms, ChatService.OptionsFrom(f.Prefs.Settings, TimeZoneInfo.Local, false), f);
        }

        /// <summary>About ten emotes grouped by the chosen way and interval; changes as soon as the setting does.</summary>
        public static void Emotes(SettingsForm f, ChatView v)
        {
            var t = SampleTime;
            var plan = new[] { new object[] { A, "heart", 0 }, new object[] { B, "star", 5 }, new object[] { A, "heart", 10 }, new object[] { A, "heart", 20 },
                               new object[] { B, "star", 25 }, new object[] { A, "star", 45 }, new object[] { A, "heart", 50 }, new object[] { B, "heart", 70 },
                               new object[] { A, "heart", 170 }, new object[] { A, "heart", 250 } };
            var ms = new List<ChatMessage>();
            for (int i = 0; i < plan.Length; i++)
            {
                var who = (string)plan[i][0];
                var emote = (string)plan[i][1];
                ms.Add(new ChatMessage
                {
                    Id = "e" + i, SenderId = who, SenderName = f.L(who == A ? "preview.nameA" : "preview.nameB"), Kind = MessageKind.Emote, EmoteId = emote,
                    Body = f.L("preview.emote." + emote), TimestampUtc = t.AddSeconds((int)plan[i][2])
                });
            }
            Show(v, Style(f), ms, ChatService.OptionsFrom(f.Prefs.Settings, TimeZoneInfo.Local, false), f);
        }

        public static void Messages(SettingsForm f, ChatView v)
        {
            var t = SampleTime;
            Show(v, Style(f), new List<ChatMessage>
            {
                Msg(f, "message-a", A, "preview.nameA", "preview.line2", t),
                new ChatMessage { Id = "meow-a", SenderId = B, SenderName = f.L("preview.nameB"), Body = MeowCatalog.Phrases[0], TimestampUtc = t.AddSeconds(20) },
                new ChatMessage { Id = "meow-b", SenderId = B, SenderName = f.L("preview.nameB"), Body = MeowCatalog.Phrases[0], TimestampUtc = t.AddSeconds(30) },
                Msg(f, "message-me", Me, "preview.me", "preview.line4", t.AddSeconds(50), true)
            }, ChatService.OptionsFrom(f.Prefs.Settings, TimeZoneInfo.Local, false), f);
        }

        /// <summary>Character preview: a normal message, the same person continuing, my lines; or the compact shape.</summary>
        public static void Characters(SettingsForm f, ChatView v, bool compactShape)
        {
            var t = SampleTime;
            var ms = new List<ChatMessage>
            {
                Msg(f, "c1", A, "preview.nameA", "preview.line1", t),
                Msg(f, "c2", A, "preview.nameA", "preview.line3", t.AddSeconds(15)),
                Msg(f, "c3", Me, "preview.me", "preview.line4", t.AddSeconds(40), true),
                Msg(f, "c4", Me, "preview.me", "preview.line6", t.AddSeconds(50), true),
            };
            if (compactShape) ms.RemoveAt(3);
            var style = Style(f);
            if (compactShape) style.Shape = ChatShape.Compact;
            Show(v, style, ms, ChatService.OptionsFrom(f.Prefs.Settings, TimeZoneInfo.Local, false), f);
        }

        /// <summary>Owned, invented drawings: no game files, network access, or installed-game dependency.</summary>
        private sealed class SamplePictures : IDisposable
        {
            private readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();
            public Image Portrait(string id, bool face)
            {
                string key = (face ? "face-" : "hat-") + id;
                Image image;
                if (_images.TryGetValue(key, out image)) return image;
                var bitmap = new Bitmap(64, 64);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(id == B ? Color.FromArgb(222, 240, 234) : Color.FromArgb(247, 229, 202));
                    int top = face ? 8 : 23;
                    using (var fill = new SolidBrush(Color.FromArgb(255, 250, 237)))
                    using (var line = new Pen(Color.FromArgb(87, 68, 61), 2))
                    using (var head = new GraphicsPath())
                    {
                        head.AddPolygon(new[] { new Point(8, top + 29), new Point(10, top), new Point(24, top + 9), new Point(42, top + 9), new Point(54, top), new Point(57, top + 29), new Point(50, top + 36), new Point(16, top + 36) });
                        g.FillPath(fill, head); g.DrawPath(line, head);
                        g.FillEllipse(Brushes.Black, 21, top + 19, 4, 5); g.FillEllipse(Brushes.Black, 40, top + 19, 4, 5);
                        g.DrawArc(line, 27, top + 24, 10, 7, 0, 180);
                    }
                    if (!face)
                    {
                        using (var hat = new SolidBrush(id == B ? Color.SteelBlue : Color.IndianRed))
                        {
                            g.FillRectangle(hat, 21, 5, 24, 16); g.FillRectangle(hat, 14, 19, 38, 5);
                        }
                    }
                }
                _images.Add(key, bitmap); return bitmap;
            }
            public Image Emote(string file)
            {
                if (file != "sample-heart" && file != "sample-star") return null;
                Image image;
                if (_images.TryGetValue(file, out image)) return image;
                var bitmap = new Bitmap(48, 48);
                using (var g = Graphics.FromImage(bitmap))
                using (var shape = new GraphicsPath())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    if (file == "sample-heart")
                    {
                        shape.AddBezier(24, 43, -9, 19, 6, -4, 24, 13);
                        shape.AddBezier(24, 13, 42, -4, 57, 19, 24, 43);
                        using (var brush = new SolidBrush(Color.IndianRed)) g.FillPath(brush, shape);
                    }
                    else
                    {
                        var points = Enumerable.Range(0, 10).Select(i => { double angle = i * Math.PI / 5 - Math.PI / 2; double radius = i % 2 == 0 ? 22 : 10; return new PointF(24 + (float)(Math.Cos(angle) * radius), 24 + (float)(Math.Sin(angle) * radius)); }).ToArray();
                        using (var brush = new SolidBrush(Color.Goldenrod)) g.FillPolygon(brush, points);
                    }
                }
                _images.Add(file, bitmap); return bitmap;
            }
            public void Dispose() { foreach (var image in _images.Values) image.Dispose(); _images.Clear(); }
        }

        public sealed class Header : Panel
        {
            private readonly Label _name = new Label { TextAlign = ContentAlignment.MiddleLeft };
            private readonly IconButton _search = new IconButton(IconKind.Search) { TabStop = false };
            private readonly IconButton _settings = new IconButton(IconKind.Gear) { TabStop = false };
            private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
            public Header() { Controls.AddRange(new Control[] { _name, _search, _settings }); }
            public void RefreshStyle(SettingsForm form)
            {
                var style = Style(form);
                string key = style.FontFamily + "/" + style.FontSize;
                Font font;
                if (!_fonts.TryGetValue(key, out font)) { font = Fonts.Create(style.FontFamily, HeaderPill.FontPoints(style.FontSize)); _fonts.Add(key, font); }
                BackColor = RenderStyle.C(style.Preset == DesignPreset.Wire ? style.Theme.Window : style.Theme.Panel);
                string nameKey = key + "/name";
                Font nameFont;
                if (!_fonts.TryGetValue(nameKey, out nameFont)) { nameFont = Fonts.Create(style.FontFamily, Math.Max(style.FontSize + 1, MainForm.StatusLinePoints(style.FontSize) + 2), FontStyle.Bold); _fonts.Add(nameKey, nameFont); }
                _name.Text = form.L("preview.nameA"); _name.Font = nameFont; _name.ForeColor = RenderStyle.C(style.Theme.Text);
                int right = Width - 14;
                foreach (var entry in new[] { new KeyValuePair<IconButton, string>(_settings, "header.btn.settings"), new KeyValuePair<IconButton, string>(_search, "header.btn.search") })
                {
                    var button = entry.Key; button.Pill = form.Prefs.Settings.Get("screen.headerButtons") == "pill";
                    button.PillText = form.L(entry.Value); button.Font = font; button.BackColor = BackColor;
                    button.PillFill = RenderStyle.C(style.Theme.OtherBubble); button.PillBorder = RenderStyle.C(style.Theme.Border);
                    button.ForeColor = RenderStyle.C(style.Theme.MidText); button.IconColor = RenderStyle.C(style.Theme.Text);
                    button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
                    button.Size = button.Pill ? new Size(HeaderPill.Width(button.PillText, font, false), HeaderPill.HeightFor(font)) : new Size(Math.Max(32, font.Height + 10), Math.Max(30, font.Height + 10));
                    right -= button.Width; button.Location = new Point(right, 6); right -= HeaderPill.WideGap;
                }
                Height = Math.Max(_settings.Bottom, _search.Bottom) + 6;
                _name.SetBounds(10, 0, Math.Max(1, right - 20), Height); Invalidate(true);
            }
            protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) foreach (var font in _fonts.Values) font.Dispose(); }
        }
    }
}
