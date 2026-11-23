using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.Domain.Appearance;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public static class SettingsPreviewTests
    {
        public static string Pixels(Control control)
        {
            using (var bitmap = new Bitmap(control.Width, control.Height))
            using (var stream = new MemoryStream())
            using (var hash = SHA256.Create())
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
                bitmap.Save(stream, ImageFormat.Png);
                return BitConverter.ToString(hash.ComputeHash(stream.ToArray()));
            }
        }

        private static ChatView Preview(SettingsForm settings) { return settings.Area.Content.Controls.OfType<ChatView>().First(); }
        private static void Key(Control c, Keys key, bool up = false)
        {
            c.GetType().GetMethod(up ? "OnKeyUp" : "OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, new object[] { new KeyEventArgs(key) });
            System.Windows.Forms.Application.DoEvents();
        }

        [Test("settings,preview")]
        public static void PreviewImagesAndHeaderChangeThroughTheirActualEditors()
        {
            using (var rig = new UiRig("preview-editors", 4))
            using (var settings = SettingsWindowTests.Open(rig))
            {
                settings.ShowCategory("chat");
                var view = Preview(settings);
                A.True(view.Rows.SelectMany(r => r.Parts).Any(p => p.Kind == NyangTalk.App.Rendering.PartKind.Image), "sample emotes have actual pictures");
                string before = Pixels(view);
                Key(settings.Editor("chat.emoteDisplay").Controls[1], Keys.Space);
                A.Eq("name", rig.Host.Prefs.Settings.Get("chat.emoteDisplay"), "radio editor changed the setting");
                A.False(before == Pixels(view), "image/name visibly differ");

                settings.ShowCategory("char"); view = Preview(settings);
                before = Pixels(view); Key(settings.Editor("char.shape").Controls[1], Keys.Space);
                A.False(before == Pixels(view), "original/circle visibly differ");
                before = Pixels(view); Key(settings.Editor("char.scope").Controls[0], Keys.Space);
                A.Eq("face", rig.Host.Prefs.Settings.Get("char.scope"), "face selected");
                A.False(before == Pixels(view), "face/hat visibly differ");

                settings.ShowCategory("people"); view = Preview(settings);
                var manual = rig.Host.Prefs.Colors.Manual.ToDictionary(p => p.Key, p => p.Value);
                before = Pixels(view); Key(settings.Editor("people.colorMode"), Keys.Down);
                A.Eq("manual", rig.Host.Prefs.Settings.Get("people.colorMode"), "manual mode selected");
                A.False(before == Pixels(view), "manual sample colours differ from auto");
                A.True(manual.OrderBy(p => p.Key).SequenceEqual(rig.Host.Prefs.Colors.Manual.OrderBy(p => p.Key)), "sample colours never change saved people");

                settings.ShowCategory("screen");
                var header = settings.Area.Content.Controls.OfType<Previews.Header>().Single();
                before = Pixels(header); Key(settings.Editor("screen.headerButtons").Controls[1], Keys.Space);
                A.Eq("pill", rig.Host.Prefs.Settings.Get("screen.headerButtons"), "pill selected");
                A.False(before == Pixels(header), "header buttons appear in the preview");

                settings.ShowCategory("chat");
                var messages = settings.Area.Content.Controls.OfType<ChatView>().Last();
                before = Pixels(messages); Key(settings.Editor("chat.hideMeows"), Keys.Space);
                A.False(before == Pixels(messages), "hide-meows has a visible message sample");
                before = Pixels(messages); Key(settings.Editor("chat.clock").Controls[1], Keys.Space);
                A.False(before == Pixels(messages), "12/24-hour clock has a visible sample");

                settings.ShowCategory("char");
                rig.Host.Prefs.Settings.Set("char.compact", true);
                A.False(settings.IsEditorEnabled("char.size"), "fixed compact size is explained rather than presenting an ineffective choice");
                A.Eq(rig.Host.Prefs.L("why.char.compactSize"), settings.Page.Rows["char.size"].Note.Text, "compact reason shown");
                rig.Host.Prefs.Settings.Set("char.compact", false);
                A.True(settings.IsEditorEnabled("char.size"), "normal avatar size can be changed again");
            }
        }

        [Test("settings,preview,DPI")]
        public static void AllPreviewRowsRemainVisibleAtLargeFontAndNarrowSettingsWidth()
        {
            using (var rig = new UiRig("preview-fit", 0))
            using (var settings = SettingsWindowTests.Open(rig, 560, 420))
            {
                foreach (var family in new[] { "pretendard", "notosanskr", "suit", "malgun" })
                foreach (int size in new[] { 8, 11, 16 })
                {
                    rig.Host.Prefs.Settings.Set("screen.font", family);
                    rig.Host.Prefs.Settings.Set("screen.fontSize", size); System.Windows.Forms.Application.DoEvents();
                    foreach (string category in new[] { "screen", "chat", "people", "char" })
                    {
                        settings.ShowCategory(category);
                        foreach (var view in settings.Area.Content.Controls.OfType<ChatView>())
                        {
                            A.True(view.Height >= view.TotalHeight, category + "/" + family + "/" + size + " shows every row");
                            A.Eq(0, view.ScrollOffset, "no samples silently hidden above");
                        }
                    }
                    for (int i = 0; i < settings.CategoryButtons.Items.Count; i++)
                    {
                        settings.ShowCategory(SettingsForm.Categories[i]);
                        var rect = settings.CategoryButtons.ItemRect(i);
                        int need = TextRenderer.MeasureText(settings.CategoryButtons.Items[i], settings.CategoryButtons.Font,
                            new Size(rect.Width - 14, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                        A.True(rect.Height >= need, "category text fits at " + family + "/" + size);
                        A.True(rect.Top >= 0 && rect.Bottom <= settings.CategoryButtons.ClientSize.Height, "selected category remains reachable");
                    }
                }
            }
        }

        [Test("settings,preview,notify")]
        public static void NotificationPreviewSurvivesNormalPollingAndRestoresUnreadState()
        {
            using (var rig = new UiRig("preview-notification", 0))
            using (var settings = SettingsWindowTests.Open(rig))
            {
                int badge = -1; bool flash = false;
                rig.Form.SetTaskbarBadge = (h, n) => badge = n;
                rig.Form.FlashTaskbar = (h, on) => flash = on;
                settings.ShowCategory("chat");
                var button = settings.Area.Content.Controls.OfType<Button>().Single(b => b.Text == rig.Host.Prefs.L("settings.notifyPreview"));
                int unread = rig.Host.Chat.Unread.Unread;
                button.PerformClick(); A.Eq(3, badge, "actual preview button sets sample badge");
                rig.Step(0.8); A.Eq(3, badge, "regular polling must not immediately clear the preview");
                A.True(flash, "preview requests flashing");
                rig.Step(3); A.Eq(unread, badge, "real badge restored after preview duration");
                A.Eq(unread, rig.Host.Chat.Unread.Unread, "real unread count never changes");
                rig.Form.PreviewNotification("flash"); A.Eq(0, badge, "flash-only preview has no sample badge");
                rig.Form.PreviewNotification("off"); A.Eq(unread, badge, "off cancels a previous preview immediately");
            }
        }

        [Test("settings,preview,opacity")]
        public static void OpacityDragPreviewSurvivesHoverTimerAndCancelsWhenSettingsClose()
        {
            using (var rig = new UiRig("preview-opacity", 0))
            {
                rig.Host.Prefs.Settings.Set("screen.opacity", 40);
                rig.Host.Prefs.Settings.Set("screen.opacityHover", true);
                rig.Form.MouseOverWindow = () => false;
                using (var settings = SettingsWindowTests.Open(rig))
                {
                    settings.ShowCategory("screen");
                    var slider = (ThemedSlider)settings.Editor("screen.opacity");
                    Key(slider, Keys.Right);
                    A.True(Math.Abs(rig.Form.Opacity - 0.45) < 0.01, "slider previews the unsaved value");
                    var timer = (Timer)typeof(MainForm).GetField("_hoverTimer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rig.Form);
                    typeof(Timer).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(timer, new object[] { EventArgs.Empty });
                    A.True(Math.Abs(rig.Form.Opacity - 0.45) < 0.01, "hover timer keeps current drag value");
                    A.Eq(40, rig.Host.Prefs.Settings.GetInt("screen.opacity"), "not committed yet");
                    settings.Close();
                }
                A.True(Math.Abs(rig.Form.Opacity - 0.40) < 0.01, "closing cancels an unfinished preview");
            }
        }

        [Test("settings,preview,portrait")]
        public static void FaceScopeUsesCompanionImageAndFallsBackForOlderConnectors()
        {
            using (var rig = new UiRig("scope-images", 0))
            {
                var file = Path.Combine(rig.Dir, "skin_hat.png");
                var face = PortraitFiles.FaceVariant(file);
                using (var image = new Bitmap(32, 32)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Red); image.Save(file, ImageFormat.Png); }
                var key = new AppearanceKey("skin", "hat");
                rig.Host.Portraits.SetLook("sample-person", key, rig.Clock.Now);
                rig.Host.Portraits.OnRendered(key, file, rig.Clock.Now);
                rig.Host.Prefs.Settings.Set("char.scope", "face");
                A.Eq(Color.Red.ToArgb(), ((Bitmap)rig.Form.Chat.Portrait("sample-person")).GetPixel(0, 0).ToArgb(), "old connector falls back to its existing portrait");
                using (var image = new Bitmap(32, 32)) { using (var g = Graphics.FromImage(image)) g.Clear(Color.Blue); image.Save(face, ImageFormat.Png); }
                A.Eq(Color.Blue.ToArgb(), ((Bitmap)rig.Form.Chat.Portrait("sample-person")).GetPixel(0, 0).ToArgb(), "new face image appears without restarting the app");
                rig.Host.Prefs.Settings.Set("char.scope", "hat");
                A.Eq(Color.Red.ToArgb(), ((Bitmap)rig.Form.Chat.Portrait("sample-person")).GetPixel(0, 0).ToArgb(), "hat scope keeps full portrait");
                rig.Form.Close();
                A.False(File.Exists(file) || File.Exists(face), "both cached variants are removed together");
            }
        }

        [Test("settings,preview,portrait")]
        public static void OlderConnectorExplainsFaceScopeWithoutBreakingChat()
        {
            using (var rig = new UiRig("scope-component", 0))
            using (var settings = SettingsWindowTests.Open(rig))
            {
                rig.Game.Producer = "0.1.0"; rig.Step();
                settings.ShowCategory("char");
                rig.Host.Prefs.Settings.Set("char.scope", "face");
                A.Eq(rig.Host.Prefs.L("char.faceNeedsComponent"), settings.Page.Rows["char.scope"].Note.Text, "optional feature upgrade is explained");
                A.True(rig.Host.Chat.Link == NyangTalk.Domain.GameLink.LinkState.Connected, "older connector still supports ordinary chat");
                rig.Game.Producer = "0.1.1"; rig.Step(); settings.Page.RefreshNotes();
                A.Eq("", settings.Page.Rows["char.scope"].Note.Text, "upgrade note clears with the new connector");
            }
        }

        [Test("settings,preview,portrait")]
        public static void PortraitVariantsShareTheSingleFrameQueueAndRepairMissingFiles()
        {
            var files = new HashSet<string>();
            var queue = new PictureQueue { FileExists = file => files.Contains(file) };
            A.Eq<string>(null, PortraitFiles.RequestPair(queue, "1_2"), "no image before rendering");
            A.Eq("1_2", queue.Next(), "first frame renders full portrait");
            files.Add("1_2.png"); queue.Succeeded("1_2", "1_2.png");
            A.Eq("1_2.png", PortraitFiles.RequestPair(queue, "1_2"), "full portrait is usable while face is queued");
            A.Eq("1_2-face", queue.Next(), "next frame renders face portrait");
            files.Add("1_2-face.png"); queue.Succeeded("1_2-face", "1_2-face.png");
            A.Eq<string>(null, queue.Next(), "repeated requests do not duplicate work");
            files.Remove("1_2-face.png"); PortraitFiles.RequestPair(queue, "1_2");
            A.Eq("1_2-face", queue.Next(), "missing companion image is recreated independently");
            A.Eq("1_2", PortraitFiles.FullKey("1_2-face"), "face variant retains the original look key");
        }
    }
}
