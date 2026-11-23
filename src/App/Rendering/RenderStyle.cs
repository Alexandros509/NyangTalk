using System;
using System.Drawing;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App.Rendering
{
    public enum ChatShape { Bubble, Card, Compact }
    public enum DesignPreset { Basic, Mobile, Business, Wire }

    /// <summary>
    /// Everything the chat list needs to know about appearance. Built from settings; the preset changes only outside
    /// shapes and decorations, the chat shape changes only the message line (30-SPEC 7장).
    /// </summary>
    public sealed class RenderStyle
    {
        public Theme Theme = Theme.Get("default");
        public ChatShape Shape = ChatShape.Bubble;
        public DesignPreset Preset = DesignPreset.Basic;
        public string FontFamily = "Malgun Gothic";
        public float FontSize = 11f;
        public bool TailBubble = true;
        public PersonColors Colors = new PersonColors();
        public DisplayNameMode NameMode = DisplayNameMode.AliasThenOriginal;
        public bool Clock24 = true;
        public bool EmoteAsImage = true;
        // character (70-SPEC 2.3)
        public bool ShowCats = true;
        public string CatSize = "normal";
        public bool CatCompact;
        public bool CatCircle;
        public bool CatFirstOnly;
        public bool ShowMyCat;
        public bool MyCatRight;
        public string Lang = "ko";

        public static RenderStyle From(Settings s, PersonColors colors, DisplayNameMode nameMode)
        {
            var r = new RenderStyle
            {
                Theme = Theme.Get(s.Get("screen.theme"), s.GetBool("screen.dark")),
                Shape = s.Get("screen.chatShape") == "card" ? ChatShape.Card : (s.Get("screen.chatShape") == "compact" ? ChatShape.Compact : ChatShape.Bubble),
                Preset = ParsePreset(s.Get("screen.preset")),
                FontFamily = FontFor(s.Get("screen.font")),
                FontSize = s.GetInt("screen.fontSize"),
                TailBubble = s.GetBool("screen.tailBubble"),
                Colors = colors,
                NameMode = nameMode,
                Clock24 = s.Get("chat.clock") == "24",
                EmoteAsImage = s.Get("chat.emoteDisplay") == "image",
                ShowCats = s.GetBool("char.show"),
                CatSize = s.Get("char.size"),
                CatCompact = s.GetBool("char.compact"),
                CatCircle = s.Get("char.shape") == "circle",
                CatFirstOnly = s.Get("char.continuation") == "first",
                ShowMyCat = s.Get("char.mine") == "show",
                MyCatRight = s.Get("char.minePosition") == "right",
                Lang = s.Get("ui.language")
            };
            return r;
        }

        public static DesignPreset ParsePreset(string v)
        {
            switch (v) { case "mobile": return DesignPreset.Mobile; case "business": return DesignPreset.Business; case "wire": return DesignPreset.Wire; default: return DesignPreset.Basic; }
        }

        public static string FontFor(string v)
        {
            switch (v)
            {
                case "pretendard": return "Pretendard";
                case "notosanskr": return "Noto Sans KR";
                case "suit": return "SUIT";
                case "segoe": return "Segoe UI";
                case "malgun": return "Malgun Gothic";
                default: return SystemFonts.MessageBoxFont.FontFamily.Name;
            }
        }

        // ---- metrics ----
        public int Pad { get { return Shape == ChatShape.Compact ? 2 : 6; } }
        public int Gutter { get { return 12; } }
        public int AvatarSize
        {
            get
            {
                if (!ShowCats) return 0;
                if (Shape == ChatShape.Compact || CatCompact) return 20;
                switch (CatSize) { case "small": return 24; case "large": return 44; default: return 32; }
            }
        }
        public int Radius
        {
            get
            {
                if (Shape == ChatShape.Compact) return 0;
                switch (Preset) { case DesignPreset.Mobile: return 16; case DesignPreset.Business: return 4; case DesignPreset.Wire: return 6; default: return 10; }
            }
        }
        public bool NameInsideBubble { get { return Preset == DesignPreset.Mobile && Shape == ChatShape.Bubble; } }
        public bool Tail { get { return Preset == DesignPreset.Mobile && Shape == ChatShape.Bubble && TailBubble; } }
        public bool OutlineOnly { get { return Preset == DesignPreset.Wire; } }
        public int InputRadius { get { switch (Preset) { case DesignPreset.Mobile: return 18; case DesignPreset.Business: return 2; case DesignPreset.Wire: return 0; default: return 6; } } }

        public static Color C(Rgb c) { return Color.FromArgb(c.R, c.G, c.B); }

        public string L(string key) { return Strings.Get(key, Lang); }
    }
}
