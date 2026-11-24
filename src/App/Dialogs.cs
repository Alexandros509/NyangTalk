using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.People;

namespace NyangTalk.App
{
    /// <summary>The one place the window icon comes from (01-OVERVIEW 1장: easy to replace before public release).</summary>
    public static class AppIcon
    {
        private static Icon _icon;

        public static Icon Load()
        {
            if (_icon != null) return _icon;
            try
            {
                // the release exe carries assets\app.ico as a resource (the same file is the exe's own icon)
                using (var s = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico"))
                    if (s != null) return _icon = new Icon(s);
                var exe = System.Reflection.Assembly.GetEntryAssembly();
                if (exe != null)
                {
                    var file = Path.Combine(Path.GetDirectoryName(exe.Location), "app.ico");
                    if (File.Exists(file)) return _icon = new Icon(file);
                    var embedded = Icon.ExtractAssociatedIcon(exe.Location);
                    if (embedded != null && HasCustomIcon) return _icon = embedded;
                }
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            return _icon = Generated();
        }

        /// <summary>Set by the release build when assets/app.ico was embedded.</summary>
        public static bool HasCustomIcon = false;

        /// <summary>Neutral placeholder: a speech bubble with "N". No game artwork.</summary>
        public static Icon Generated()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var path = ChatPainter.Rounded(new Rectangle(1, 1, 30, 24), 8))
                    using (var b = new SolidBrush(Color.FromArgb(0x3A, 0x57, 0xC4))) { g.FillPath(b, path); g.FillPolygon(b, new[] { new Point(8, 24), new Point(8, 31), new Point(15, 24) }); }
                    using (var f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, "N", f, new Rectangle(1, 1, 30, 24), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    /// <summary>
    /// Secondary windows open on the monitor of the window they belong to (2026-11-20 user request): centred on the owner,
    /// kept inside that monitor's work area. Windows shown with Show() ignore CenterParent, so every window is placed here.
    /// </summary>
    public static class Placement
    {
        /// <summary>Prefer a full-size adjacent window, then a smaller adjacent one, then the least overlap.</summary>
        public static Rectangle SettingsBounds(Rectangle owner, Size desired, Size minimum, Rectangle work, int gap)
        {
            var size = new Size(Math.Min(desired.Width, work.Width), Math.Min(desired.Height, work.Height));
            int y = Math.Max(work.Top, Math.Min(owner.Top, work.Bottom - size.Height));
            int x = Math.Max(work.Left, Math.Min(owner.Left, work.Right - size.Width));
            var full = new[] { new Rectangle(owner.Right + gap, y, size.Width, size.Height),
                new Rectangle(owner.Left - gap - size.Width, y, size.Width, size.Height),
                new Rectangle(x, owner.Bottom + gap, size.Width, size.Height),
                new Rectangle(x, owner.Top - gap - size.Height, size.Width, size.Height) };
            foreach (var r in full) if (work.Contains(r)) return r;
            int right = work.Right - owner.Right - gap, left = owner.Left - work.Left - gap;
            if (right >= minimum.Width) { var r = new Rectangle(owner.Right + gap, y, Math.Min(size.Width, right), size.Height); if (work.Contains(r)) return r; }
            if (left >= minimum.Width) { var r = new Rectangle(work.Left + Math.Max(0, left - size.Width), y, Math.Min(size.Width, left), size.Height); if (work.Contains(r)) return r; }
            int below = work.Bottom - owner.Bottom - gap, above = owner.Top - work.Top - gap;
            if (below >= minimum.Height) { var r = new Rectangle(x, owner.Bottom + gap, size.Width, Math.Min(size.Height, below)); if (work.Contains(r)) return r; }
            if (above >= minimum.Height) { var r = new Rectangle(x, work.Top + Math.Max(0, above - size.Height), size.Width, Math.Min(size.Height, above)); if (work.Contains(r)) return r; }
            var candidates = full.Concat(new[] { new Rectangle(work.Left, work.Top, size.Width, size.Height),
                new Rectangle(work.Right - size.Width, work.Top, size.Width, size.Height),
                new Rectangle(work.Left, work.Bottom - size.Height, size.Width, size.Height),
                new Rectangle(work.Right - size.Width, work.Bottom - size.Height, size.Width, size.Height) })
                .Select(r => new Rectangle(Math.Max(work.Left, Math.Min(r.X, work.Right - size.Width)),
                    Math.Max(work.Top, Math.Min(r.Y, work.Bottom - size.Height)), size.Width, size.Height));
            return candidates.OrderBy(r => { var overlap = Rectangle.Intersect(owner, r); return (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height); })
                .ThenBy(r => Math.Abs(r.Top - owner.Top)).First();
        }

        public static void Beside(Form settings, Form owner)
        {
            if (owner == null || owner.WindowState == FormWindowState.Minimized) return;
            var work = Screen.FromControl(owner).WorkingArea;
            settings.StartPosition = FormStartPosition.Manual;
            settings.MinimumSize = new Size(Math.Min(settings.MinimumSize.Width, work.Width), Math.Min(settings.MinimumSize.Height, work.Height));
            settings.Bounds = SettingsBounds(owner.Bounds, settings.Size, settings.MinimumSize, work,
                Platform.DisplayScale.Pixels(12, Platform.DisplayScale.Dpi(owner)));
        }

        public static Point CenterOn(Rectangle owner, Size size, Rectangle workArea)
        {
            int x = owner.Left + (owner.Width - size.Width) / 2, y = owner.Top + (owner.Height - size.Height) / 2;
            x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - size.Width));
            y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - size.Height));
            return new Point(x, y);
        }

        /// <summary>Places <paramref name="f"/> over its owner (or the given window) before it is shown.</summary>
        public static void Over(Form f, IWin32Window owner)
        {
            var o = owner as Control ?? (owner == null ? null : Control.FromHandle(owner.Handle));
            var top = o == null ? null : o.FindForm() ?? o as Form;
            if (top == null) return;
            if (top.WindowState == FormWindowState.Minimized) return;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = CenterOn(top.Bounds, f.Size, Screen.FromControl(top).WorkingArea);
        }
    }

    public static class Dialogs
    {
        private static string L(PreferencesService p, string k) { return p.L(k); }

        public static void EditAlias(IWin32Window owner, PreferencesService prefs, string steamId, string original)
        {
            using (var f = new Form { Text = prefs.L("menu.alias"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 120), MinimizeBox = false, MaximizeBox = false, Tag = "popup" })
            {
                var pal = Themed(f, prefs);
                var lbl = new Label { Text = original + " — " + prefs.L("people.aliasPrompt"), Location = new Point(12, 12), Size = new Size(296, 20), AutoEllipsis = true };
                var box = new TextBox { Location = new Point(12, 36), Size = new Size(296, 24), Text = prefs.Aliases.Get(steamId) ?? "", MaxLength = 40, AccessibleName = prefs.L("people.aliasPrompt") };
                var err = new Label { Location = new Point(12, 62), Size = new Size(296, 18), ForeColor = pal.Warning, Tag = "warn" };
                var ok = new Button { Text = "OK", Location = new Point(152, 84), Size = new Size(74, 28), DialogResult = DialogResult.None };
                var clear = new Button { Text = prefs.L("send.delete"), Location = new Point(234, 84), Size = new Size(74, 28) };
                ok.Click += (s, e) =>
                {
                    var res = prefs.SetAlias(steamId, box.Text);
                    if (res != AliasError.None) { err.Text = prefs.L("people.aliasInvalid"); return; }
                    f.DialogResult = DialogResult.OK;
                };
                clear.Click += (s, e) => { prefs.RemoveAlias(steamId); f.DialogResult = DialogResult.OK; };
                f.AcceptButton = ok;
                f.Controls.AddRange(new Control[] { lbl, box, err, ok, clear });
                Placement.Over(f, owner);
                f.ShowDialog(owner);
            }
        }

        public static void PickColor(IWin32Window owner, PreferencesService prefs, string steamId)
        {
            using (var f = new Form { Text = prefs.L("menu.color"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(6 * 40 + 16, 2 * 40 + 16), MinimizeBox = false, MaximizeBox = false, Tag = "popup" })
            {
                Themed(f, prefs);
                for (int i = 0; i < PersonColors.Palette.Length; i++)
                {
                    int idx = i;
                    var b = new Button { Location = new Point(8 + (i % 6) * 40, 8 + (i / 6) * 40), Size = new Size(36, 36), BackColor = RenderStyle.C(PersonColors.Palette[i]), FlatStyle = FlatStyle.Flat, AccessibleName = PersonColors.Palette[i].ToHex(), Tag = "swatch" };
                    b.Click += (s, e) =>
                    {
                        if (prefs.Settings.Get("people.colorMode") != "manual") prefs.Settings.Set("people.colorMode", "manual");
                        prefs.SetManualColor(steamId, idx);
                        f.Close();
                    };
                    f.Controls.Add(b);
                }
                Placement.Over(f, owner);
                f.ShowDialog(owner);
            }
        }

        /// <summary>Theme colours for a small window: background, text, title bar (90-SPEC 1.3, 1.6).</summary>
        public static Palette Themed(Form f, PreferencesService prefs)
        {
            var th = RenderStyle.From(prefs.Settings, prefs.Colors, prefs.NameMode).Theme;
            var pal = Palette.From(th);
            f.BackColor = pal.Panel; f.ForeColor = pal.Text;
            f.HandleCreated += (s, e) => NyangTalk.App.Platform.TitleBar.Apply(new NyangTalk.App.Platform.RealDwm(), f.Handle, th, th.Border);
            f.Load += (s, e) => Paint(f, pal);
            return pal;
        }

        /// <summary>Labels, text boxes and buttons of a small window in theme colours (no Windows grey).</summary>
        public static void Paint(Control root, Palette pal)
        {
            foreach (Control c in root.Controls)
            {
                var b = c as Button;
                if (b != null && b.Tag as string != "swatch") { pal.StyleButton(b); }
                else if (c is TextBox) { c.BackColor = pal.Input; c.ForeColor = pal.InputText; ((TextBox)c).BorderStyle = BorderStyle.FixedSingle; }
                else if (c is Label && c.Tag as string != "warn") { c.BackColor = root.BackColor; if (c.ForeColor != pal.Warning) c.ForeColor = pal.Text; }
                Paint(c, pal);
            }
        }

        public static Form GlobalSearch(MainForm owner, AppHost host, Action<SearchHit> open)
        {
            var f = new Form { Text = host.Prefs.L("search.global"), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(480, 400), MinimizeBox = false, Tag = "popup", Owner = owner, ShowInTaskbar = false, KeyPreview = true, Font = owner.Font };
            var pal = Themed(f, host.Prefs);
            var box = new TextBox { Dock = DockStyle.Top, AccessibleName = host.Prefs.L("search.global"), BackColor = pal.Input, ForeColor = pal.InputText, BorderStyle = BorderStyle.FixedSingle };
            var list = new ThemedList { Dock = DockStyle.Fill, AccessibleName = host.Prefs.L("search.global") };
            list.Pal = pal;
            var hits = new System.Collections.Generic.List<SearchHit>();
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                hits = host.Chat.SearchAll(box.Text, 200);
                list.BeginUpdate(); list.Items.Clear();
                foreach (var h in hits) list.Items.Add((h.RoomName ?? h.LobbyId) + " · " + h.Message.TimestampUtc.ToLocalTime().ToString("MM-dd HH:mm") + " · " + h.Message.SenderName + ": " + h.Message.Body.Replace('\n', ' '));
                if (hits.Count == 0 && box.Text.Length > 0) list.Items.Add(host.Prefs.L("search.none"));
                list.EndUpdate();
            };
            box.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
            Action go = () => { int i = list.SelectedIndex; if (i >= 0 && i < hits.Count) { open(hits[i]); f.Close(); } };
            list.DoubleClick += (s, e) => go();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) go(); };
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.Focus(); list.SelectedIndex = 0; } };
            f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
            f.FormClosed += (s, e) => timer.Dispose();
            f.Controls.Add(list); f.Controls.Add(box);
            Placement.Over(f, owner);
            f.Show(owner);
            box.Focus();
            return f;
        }

        /// <summary>95-SPEC 2.7: lets the user pick saved rooms from the same gathering to view together.</summary>
        public static Form GroupRooms(MainForm owner, AppHost host, string seedLobbyId, Action done)
        {
            var rooms = host.History.ListRooms().Where(r => r.LobbyId != host.Chat.CurrentLobbyId).ToList();
            var preselected = new System.Collections.Generic.HashSet<string>(host.Chat.RoomGroups.MembersOf(seedLobbyId), StringComparer.Ordinal);
            var f = new Form { Text = host.Prefs.L("room.groupTitle"), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 320), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Tag = "popup", Owner = owner, ShowInTaskbar = false, Font = owner.Font };
            var pal = Themed(f, host.Prefs);
            var checks = new System.Collections.Generic.List<KeyValuePair<string, ThemedCheck>>();
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
            int y = 8;
            foreach (var r in rooms)
            {
                var name = string.IsNullOrEmpty(r.Name) ? string.Format(host.Prefs.L("room.unknownName"), NyangTalk.Domain.Lobby.LobbyInfo.IdSuffix(r.LobbyId)) : r.Name;
                var cb = new ThemedCheck { Text = name + " · " + r.LastUtc.ToLocalTime().ToString("MM-dd HH:mm") + " (" + r.MessageCount + ")", Checked = preselected.Contains(r.LobbyId), Pal = pal, Location = new Point(12, y), Size = new Size(380, 26) };
                panel.Controls.Add(cb);
                checks.Add(new System.Collections.Generic.KeyValuePair<string, ThemedCheck>(r.LobbyId, cb));
                y += 30;
            }
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var ok = new Button { Text = host.Prefs.L("room.groupConfirm"), Size = new Size(96, 30), Location = new Point(312, 7) };
            ok.Click += (s, e) =>
            {
                var ids = checks.Where(c => c.Value.Checked).Select(c => c.Key).ToList();
                if (ids.Count >= 2) { host.Chat.GroupRooms(ids); if (done != null) done(); }
                f.Close();
            };
            bar.Controls.Add(ok);
            f.Controls.Add(panel);
            f.Controls.Add(bar);
            Placement.Over(f, owner);
            f.Show(owner);
            return f;
        }
    }
}
