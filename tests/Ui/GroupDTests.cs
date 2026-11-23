using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.App.Controls;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;
using LinkState = NyangTalk.Domain.GameLink.LinkState;

namespace NyangTalk.Tests
{
    /// <summary>95-SPEC 2.7 room groups, 2.8 last participants when disconnected, 2.9 first-run language screen, 2.10 four fonts.</summary>
    public static class GroupDTests
    {
        [Test("95-2.7")]
        public static void SavedRoomsGroupUngroupSearchExportAndRetention()
        {
            using (var rig = new UiRig("room-groups", 0))
            {
                var h = rig.Host.History;
                var chat = rig.Host.Chat;
                var tOld = rig.Clock.Now.AddDays(-40);
                var tRecent = rig.Clock.Now.AddDays(-2);

                // Two past rooms with the exact same name, plus a third room: never merged automatically by name
                h.Append("lobby_part1", "단골 모임", new[]
                {
                    new ChatMessage { Id = "m1", LobbyId = "lobby_part1", SenderId = "76561190000000002", SenderName = "보리", Body = "첫째 날 첫인사", Kind = MessageKind.Text, TimestampUtc = tOld }
                });
                h.Append("lobby_part2", "단골 모임", new[]
                {
                    new ChatMessage { Id = "m2", LobbyId = "lobby_part2", SenderId = "76561190000000003", SenderName = "콩이", Body = "둘째 날 이어진 이야기", Kind = MessageKind.Text, TimestampUtc = tRecent }
                });
                h.Append("lobby_other", "다른 방", new[]
                {
                    new ChatMessage { Id = "m3", LobbyId = "lobby_other", SenderId = "76561190000000004", SenderName = "치즈", Body = "딴 방 대화", Kind = MessageKind.Text, TimestampUtc = tRecent }
                });

                var before = chat.ListArchivedRooms();
                A.Eq(3, before.Count, "same-named rooms are not merged automatically");
                var file1 = Path.Combine(rig.Dir, "history", "lobby_part1.ndjson");
                var file2 = Path.Combine(rig.Dir, "history", "lobby_part2.ndjson");
                var raw1 = File.ReadAllText(file1);
                var raw2 = File.ReadAllText(file2);

                // Group the two rooms from the same gathering
                var g = chat.GroupRooms(new[] { "lobby_part1", "lobby_part2" });
                A.True(g != null && g.LobbyIds.Count == 2, "grouped");
                A.Eq(raw1, File.ReadAllText(file1), "original record file 1 untouched");
                A.Eq(raw2, File.ReadAllText(file2), "original record file 2 untouched");

                var groupedList = chat.ListArchivedRooms();
                A.Eq(2, groupedList.Count, "folded into one entry in the saved list");
                var entry = groupedList.Single(r => r.IsGrouped);
                A.Eq(2, entry. MessageCount, "combined message count");
                rig.Form.RefreshArchive();
                A.True(rig.Form.RoomLabel(entry).Contains(string.Format(rig.Host.Prefs.L("room.groupedCount"), 2)), "shows grouped badge: " + rig.Form.RoomLabel(entry));

                // Persisted in app data across restarts
                var reloaded = new AppHost(rig.Dir, rig.Clock, false);
                A.Eq(1, reloaded.Chat.RoomGroups.All.Count, "saved in app data");
                A.Eq(2, reloaded.Chat.RoomGroups.MembersOf("lobby_part1").Count, "reloaded group members");

                // Open the grouped room: both rooms' messages appear together in chronological order
                chat.OpenArchived(entry.LobbyId, entry.Name);
                rig.Form.RefreshChat();
                A.Eq(2, chat.Messages.Count, "both rooms loaded together");
                A.Eq("첫째 날 첫인사", chat.Messages[0].Body, "older room first");
                A.Eq("둘째 날 이어진 이야기", chat.Messages[1].Body, "newer room second");

                // In-room search and global search find messages from both rooms in the group
                rig.Form.SearchBar.Visible = true;
                rig.Form.SearchBox.Text = "첫인사";
                rig.Form.RunSearch();
                A.Eq(1, rig.Form.Search.Hits.Count, "in-room search finds message from earlier room in the group");
                var globalHits = chat.SearchAll("첫인사", 20);
                A.Eq(1, globalHits.Count, "global search finds message in grouped room");

                // Export includes messages from both rooms in the group
                var exported = chat.Export(TimeZoneInfo.Local);
                A.True(exported.Contains("첫째 날 첫인사") && exported.Contains("둘째 날 이어진 이야기"), "export contains the whole grouped conversation");

                // Ungrouping restores separate rooms without touching files
                A.True(chat.UngroupRooms(entry.LobbyId), "ungrouped");
                A.Eq(3, chat.ListArchivedRooms().Count, "separate again after ungrouping");
                chat.OpenArchived("lobby_part2", "단골 모임");
                A.Eq(1, chat.Messages.Count, "single room loads only its own messages after ungrouping");

                // Group again, then run 30-day retention cleanup: lobby_part1 (40 days old) expires, lobby_part2 (2 days old) stays
                chat.GroupRooms(new[] { "lobby_part1", "lobby_part2" });
                h.Cleanup(rig.Clock.Now, 30);
                A.False(File.Exists(file1), "expired room file cleaned up");
                A.True(File.Exists(file2), "recent room file kept");
                var afterCleanup = chat.ListArchivedRooms();
                var surviving = afterCleanup.Single(r => r.LobbyId == "lobby_part2");
                A.Eq(1, surviving.MessageCount, "remaining side still shown after retention cleanup");
                chat.OpenArchived(surviving.LobbyId, surviving.Name);
                A.Eq("둘째 날 이어진 이야기", chat.Messages.Single().Body, "remaining side's messages still readable");
            }
        }

        [Test("95-2.8")]
        public static void ParticipantsShowLastListInGreyWhenDisconnectedAndLiveWhenReconnected()
        {
            using (var rig = new UiRig("last-people", 4))
            {
                var f = rig.Form;
                f.OpenParticipants();
                A.Eq(LinkState.Connected, rig.Host.Chat.Link, "connected initially");
                A.False(f.People.Stale, "live list is not stale");
                A.False(f.People.List.Muted, "live list is not greyed");
                A.False(f.People.Note.Visible, "no disconnected note while connected");
                int countBefore = f.People.List.Items.Count;
                A.True(countBefore >= 3, "participants shown");

                // Game stops updating status (> 6 s) -> Disconnected
                rig.Clock.Now = rig.Clock.Now.AddSeconds(8);
                rig.Host.Chat.Poll();
                f.UpdateHeader();
                A.Eq(LinkState.Disconnected, rig.Host.Chat.Link, "disconnected");
                A.True(f.People.Stale, "marked as last known list");
                A.True(f.People.List.Muted, "list items greyed out");
                A.Eq( countBefore, f.People.List.Items.Count, "last known members kept on screen");
                A.True(f.People.Note.Visible && f.People.Note.Text.StartsWith(rig.Host.Prefs.L("people.lastSeen").Substring(0, 6)), "shows 마지막으로 확인한 명단 · 시각: " + f.People.Note.Text);
                var pal = Palette.From(f.CurrentStyle.Theme);
                A.Eq(pal.Sub.ToArgb(), f.People.Note.ForeColor.ToArgb(), "note in grey colour");
                A.Eq(pal.Sub.ToArgb(), f.People.Title.ForeColor.ToArgb(), "title in grey colour");

                // Game reconnects -> back to live list
                rig.Step(1);
                A.Eq(LinkState.Connected, rig.Host.Chat.Link, "reconnected");
                A.False(f.People.Stale, "live again");
                A.False(f.People.List.Muted, "not greyed");
                A.False(f.People.Note.Visible, "last-seen note hidden");
                A.Eq(pal.Text.ToArgb(), f.People.Title.ForeColor.ToArgb(), "title back to primary text colour");
            }
        }

        [Test("95-2.9")]
        public static void FirstRunAsksLanguageInWindowWithKoreanPreselectedAndOnceOnly()
        {
            var oldCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                // Even on an English or Japanese Windows, Korean is pre-selected on the first run
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                var dir = Ctx.NewDir("first-run-lang");
                using (var rig1 = new UiRig("first-run-1", 0, dir, firstRun: true))
                {
                    A.True(rig1.Form.FirstRunPanel.Visible, "shown on the first run inside the main window");
                    A.Eq(rig1.Form, rig1.Form.FirstRunPanel.FindForm(), "inside the main window, not a separate popup");
                    A.True(rig1.Form.FirstRunKo.Checked && !rig1.Form.FirstRunEn.Checked && !rig1.Form.FirstRunJa.Checked, "Korean is pre-selected regardless of Windows language");
                    A.Eq("ko", rig1.Form.SelectedFirstRunLanguage, "selected language is ko");

                    // Pick English and start
                    rig1.Form.FirstRunEn.Checked = true;
                    A.Eq("en", rig1.Form.SelectedFirstRunLanguage, "radio selects English");
                    rig1.Form.FirstRunStart.PerformClick();
                    A.False(rig1.Form.FirstRunPanel.Visible, "first-run screen closes at once");
                    A.Eq("en", rig1.Host.Prefs.Lang, "language applied immediately");
                    A.Eq(Strings.Get("send.hint", "en"), rig1.Form.HintLabel.Text, "main window text is now in English");
                    A.True(File.Exists(Path.Combine(dir, "data", "settings.json")), "saved to settings.json");
                }

                // Second run on the same data folder: never shown again, saved language kept
                using (var rig2 = new UiRig("first-run-2", 0, dir, firstRun: true))
                {
                    A.False(rig2.Host.Prefs.FirstRun, "not first run anymore");
                    A.False(rig2.Form.FirstRunPanel.Visible, "hidden on second run");
                    A.Eq("en", rig2.Host.Prefs.Lang, "saved language loaded");
                }
            }
            finally { Thread.CurrentThread.CurrentUICulture = oldCulture; }
        }

        [Test("95-2.10")]
        public static void FourFontsIncludingMalgunWindowsDefaultHaveNoClipping()
        {
            var def = Settings.Def("screen.font");
            A.Eq("pretendard", def.Default, "Pretendard remains the default");
            A.Eq("pretendard,notosanskr,suit,malgun", string.Join(",", def.Choices), "4 font choices including Malgun Gothic");
            A.Eq("맑은 고딕(Windows 기본)", Strings.ValueLabel("screen.font", "malgun", "ko"), "Korean label for Malgun Gothic");

            using (var rig = new UiRig("four-fonts", 8))
            {
                var outgoing = new OutgoingMessage { RequestId = "r1", Text = "글꼴 검사 메시지", State = OutgoingState.Unconfirmed, CreatedUtc = Fake.T0 };
                foreach (var fontKey in def.Choices)
                {
                    rig.Host.Prefs.Settings.Set("screen.font", fontKey);
                    foreach (var width in new[] { 380, 510, 820 })
                    {
                        rig.Resize(width, 720);
                        rig.Step();
                        HeaderChecks.NoOverlap(rig.Form, fontKey + "/" + width);
                        int chatW = rig.Form.Chat.ClientSize.Width;
                        foreach (var row in rig.Form.Chat.Rows)
                        {
                            var errs = RowChecks.Check(row, chatW);
                            A.True(errs.Count == 0, fontKey + "/" + width + " chat row clipping: " + string.Join("; ", errs));
                        }
                        var outRow = rig.Form.Chat.Layout2.OutgoingRow(outgoing, chatW, rig.Form.CurrentStyle);
                        A.Eq(0, RowChecks.Check(outRow, chatW).Count, fontKey + "/" + width + " outgoing row");
                    }
                    using (var sf = SettingsWindowTests.Open(rig))
                    {
                        foreach (var cat in new[] { "screen", "chat", "people", "link", "about" })
                        {
                            sf.ShowCategory(cat);
                            A.True(sf.Area.Content.Width <= sf.Area.ClientSize.Width, fontKey + "/" + cat + ": no horizontal overflow in settings");
                        }
                    }
                    // Also check both header button styles ("icon" and "pill") have no overlap at any width
                    foreach (var btnStyle in new[] { "icon", "pill" })
                    {
                        rig.Host.Prefs.Settings.Set("screen.headerButtons", btnStyle);
                        foreach (var w in new[] { 300, 380, 510, 820 })
                        {
                            rig.Resize(w, 720);
                            rig.Step();
                            HeaderChecks.NoOverlap(rig.Form, "headerButtons=" + btnStyle + "/" + w);
                        }
                    }
                    rig.Host.Prefs.Settings.Set("screen.headerButtons", "icon");
                }
            }
        }
    }
}
