using System;
using System.Collections.Generic;
using NyangTalk.App.Controls;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Sending;

namespace NyangTalk.App
{
    /// <summary>
    /// Invented sample conversations for the settings previews (90-SPEC 1.4). Names and sentences come from the
    /// translation table, so they follow the app language ("보리", "콩이" in Korean).
    /// </summary>
    public static class Previews
    {
        private const string A = "preview-a", B = "preview-b", Me = "preview-me";

        private static ChatMessage Msg(SettingsForm f, string id, string sender, string nameKey, string bodyKey, DateTime t, bool mine = false)
        {
            return new ChatMessage { Id = id, SenderId = sender, SenderName = f.L(nameKey), Body = f.L(bodyKey), TimestampUtc = t, IsMine = mine };
        }

        private static void Show(ChatView v, RenderStyle style, List<ChatMessage> ms, TimelineOptions opt, SettingsForm f)
        {
            v.Style = style;
            v.Layout2.AliasOf = id => id == A ? f.L("preview.aliasA") : null;
            v.Layout2.EmoteOf = id => new EmoteInfo { Name = f.L("preview.emote." + id) };
            v.SetContent(TimelineBuilder.Build(ms, opt), new List<OutgoingMessage>(), false, false);
        }

        private static RenderStyle Style(SettingsForm f) { return RenderStyle.From(f.Prefs.Settings, f.Prefs.Colors, f.Prefs.NameMode); }

        /// <summary>Normal conversation: other people, a consecutive message, my message.</summary>
        public static void Conversation(SettingsForm f, ChatView v, bool people)
        {
            var t = DateTime.UtcNow.AddMinutes(-5);
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
            var t = DateTime.UtcNow.AddMinutes(-10);
            var plan = new[] { new object[] { A, "heart", 0 }, new object[] { A, "heart", 4 }, new object[] { B, "star", 9 }, new object[] { A, "heart", 12 },
                               new object[] { B, "star", 20 }, new object[] { A, "star", 26 }, new object[] { A, "heart", 41 }, new object[] { B, "heart", 47 },
                               new object[] { B, "heart", 120 }, new object[] { A, "heart", 230 } };
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

        /// <summary>Character preview: a normal message, the same person continuing, my lines; or the compact shape.</summary>
        public static void Characters(SettingsForm f, ChatView v, bool compactShape)
        {
            var t = DateTime.UtcNow.AddMinutes(-5);
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
    }
}
