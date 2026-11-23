using System;
using System.Collections.Generic;

namespace NyangTalk.Domain.Conversation
{
    public enum MessageKind
    {
        Text = 0,
        Emote = 1,
        /// <summary>Any other kind the game distinguishes; displayed like text.</summary>
        Other = 2
    }

    /// <summary>One lobby chat message as received from the game (40-SPEC 2장).</summary>
    public sealed class ChatMessage
    {
        public string Id;
        public string LobbyId;
        public string SenderId;     // Steam ID as decimal string
        public string SenderName;   // name the game showed at the time
        public string Body;         // kept verbatim (50-SPEC 1장)
        public MessageKind Kind;
        public string EmoteId;      // when Kind == Emote
        public DateTime TimestampUtc;
        public bool IsMine;
        /// <summary>Game run the component was in when it received the line (pairing requires the same run).</summary>
        public string GameSession;

        public bool IsEmote { get { return Kind == MessageKind.Emote; } }
    }

    /// <summary>
    /// The automatic cry phrases the game posts when a cat is clicked (50-SPEC 3장).
    /// Fixed list from the game's translation table (game build 25936063, checked 2026-10-28 by the spec author).
    /// Matching is exact: no trimming, no case folding, no partial match.
    /// </summary>
    public static class MeowCatalog
    {
        public static readonly string[] Phrases = new[]
        {
            "야옹 야옹", "Meow Meow", "にゃんにゃ～ん", "喵~喵~", "喵嗚～", "Miaou Miaou", "Miau Miau", "Miau miau",
            "Mňau Mňau", "Mjav Mjau", "Miauw Miauw", "Miu Mau", "Νιάου Νιάου", "מיאו מיאו", "Miao Miao", "Mjau Mjau",
            "Мяу Мяу", "Miyav Miyav", "Няв Няв"
        };

        private static readonly HashSet<string> Set = new HashSet<string>(Phrases, StringComparer.Ordinal);

        public static bool IsMeow(ChatMessage m)
        {
            if (m == null || m.IsEmote || m.Body == null) return false;
            return Set.Contains(m.Body);
        }
    }
}
