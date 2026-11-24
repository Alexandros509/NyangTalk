using System;
using System.Linq;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Tests
{
    public static class CharacterSizeTests
    {
        [Test("character,size,layout")]
        public static void LargerCharactersFitNarrowMessagesAndKeepCompactRowsSmall()
        {
            var keys = new[] { "small", "normal", "large" };
            var pixels = new[] { 44, 60, 80 };
            using (var m = new Measurer())
            {
                var layout = new ChatLayout(m);
                for (int k = 0; k < keys.Length; k++)
                foreach (int width in new[] { 260, 330, 510 })
                foreach (int font in new[] { 8, 11, 16 })
                foreach (var shape in new[] { ChatShape.Bubble, ChatShape.Card })
                foreach (DesignPreset preset in Enum.GetValues(typeof(DesignPreset)))
                foreach (int mode in new[] { 0, 1, 2, 3 })
                {
                    bool mine = mode >= 2;
                    var style = new RenderStyle { Theme = Theme.Get("peach"), FontSize = font, Shape = shape, Preset = preset,
                        CatSize = keys[k], ShowMyCat = true, MyCatRight = mode == 3, CatFirstOnly = true };
                    var message = new ChatMessage { Id = "example", SenderId = mine ? "me" : "sample", SenderName = "보리",
                        Body = "캐릭터를 크게 해도 글과 시간이 겹치지 않아야 해요.\n다음 줄도 확인해요.", TimestampUtc = Fake.T0, IsMine = mine };
                    var row = layout.Lay(new MessageItem { Message = message, TimestampUtc = Fake.T0, IsContinuation = mode == 1 }, width, style);
                    string label = keys[k] + "/" + width + "/" + font + "/" + shape + "/" + preset + "/" + mode;
                    A.Eq(0, RowChecks.Check(row, width).Count, label + " stays inside its row without overlap");
                    var avatar = row.Parts.SingleOrDefault(p => p.Kind == PartKind.Avatar);
                    if (mode == 1) A.True(avatar == null, "first-only continuation keeps the avatar hidden");
                    else
                    {
                        A.True(avatar != null, label + " shows the character");
                        A.Eq(pixels[k], avatar.Rect.Width, label + " applies the selected character size");
                        A.Eq(pixels[k], avatar.Rect.Height, label + " keeps the character square");
                    }
                }
                foreach (string key in keys)
                foreach (bool compactShape in new[] { false, true })
                {
                    var style = new RenderStyle { CatSize = key, CatCompact = !compactShape, Shape = compactShape ? ChatShape.Compact : ChatShape.Bubble };
                    var message = new ChatMessage { Id = "compact", SenderId = "sample", SenderName = "보리", Body = "안녕", TimestampUtc = Fake.T0 };
                    var row = layout.Lay(new MessageItem { Message = message, TimestampUtc = Fake.T0 }, 330, style);
                    A.Eq(20, row.Parts.Single(p => p.Kind == PartKind.Avatar).Rect.Width, "compact characters retain their small size");
                    A.Eq(0, RowChecks.Check(row, 330).Count, "compact rows fit");
                }
            }
        }
    }
}
