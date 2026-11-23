using System.Collections.Generic;
using NyangTalk.Domain.People;

namespace NyangTalk.Domain.Preferences
{
    /// <summary>Colour theme using the user-approved reference palette.</summary>
    public sealed class Theme
    {
        public string Id;
        public Rgb Window;       // app background
        public Rgb Panel;        // sidebar/header
        public Rgb Text;         // body text
        public Rgb SubText;      // time, hints
        public Rgb OtherBubble;
        public Rgb OtherText;
        public Rgb MineBubble;
        public Rgb MineText;
        public Rgb Accent;       // buttons
        public Rgb AccentText;
        public Rgb Border;
        public Rgb Notice;       // date separator and notice text
        public Rgb Soft, MidText, FaintText, DatePill;
        public bool Dark;
        /// <summary>Warning text on the window background (failed actions, test update address, 완전 제거).</summary>
        public Rgb Warning { get { return Dark ? Rgb.FromHex("#FF9C8A") : Rgb.FromHex("#B3261E"); } }

        public static readonly string[] Ids = { "default", "mint", "sky", "purple", "peach", "mono" };

        public static Theme Get(string id) { return Get(id, false); }

        /// <summary>90-SPEC 1.6: every colour theme has a light and a dark variant (6 × 2).</summary>
        public static Theme Get(string id, bool dark)
        {
            if (!dark) return Light(id);
            Theme t;
            switch (id)
            {
                case "mint": t = Make("mint", "#0F1714", "#17221D", "#22302A", "#1D2A24", "#2F4037", "#E9F2ED", "#C5D5CC", "#9DB3A7", "#8BA196", "#22332B", "#B5CDBF", "#1B7F53", "#FFFFFF"); break;
                case "sky": t = Make("sky", "#0E1621", "#17212B", "#222E3C", "#1C2835", "#2E3D4E", "#EAF1F8", "#C6D3E0", "#9CAFC3", "#8A9DB2", "#223246", "#B3C8DD", "#1F6FD0", "#FFFFFF"); break;
                case "purple": t = Make("purple", "#14111D", "#1D1929", "#2A2539", "#231F31", "#3A3350", "#F0ECF8", "#D0C9E0", "#A9A0C0", "#968DAF", "#2B2540", "#C3B8DD", "#6A4BD6", "#FFFFFF"); break;
                case "peach": t = Make("peach", "#1B1314", "#261B1C", "#35272A", "#2D2123", "#4A373A", "#F8ECEA", "#E0CBC8", "#BFA3A0", "#AC918E", "#36272A", "#DDBFBB", "#C9365B", "#FFFFFF"); break;
                case "mono": t = Make("mono", "#101010", "#1A1A1A", "#262626", "#202020", "#383838", "#F2F2F2", "#D0D0D0", "#A6A6A6", "#909090", "#262626", "#C8C8C8", "#EDEDED", "#1A1A1A"); break;
                default: t = Make("default", "#131922", "#1D2530", "#2A3544", "#253140", "#354354", "#EEF3F8", "#CBD6E1", "#A4B5C6", "#93A5B7", "#293746", "#B9CBD9", "#FFE36B", "#293B46"); break;
            }
            t.Dark = true;
            return t;
        }

        private static Theme Light(string id)
        {
            switch (id)
            {
                case "mint": return Make("mint", "#E7EFE9", "#F7FAF8", "#FFFFFF", "#DCE8E0", "#D3DED7", "#1F2D27", "#3D5248", "#586E63", "#6F8378", "#C9DCD1", "#3F5A4D", "#1B7F53", "#FFFFFF");
                case "sky": return Make("sky", "#E6EEF7", "#F8FAFD", "#FFFFFF", "#D9E5F2", "#D2DCE8", "#1C2A3A", "#3A4E66", "#566C86", "#6C7F96", "#C7D8EA", "#3C5878", "#1F6FD0", "#FFFFFF");
                case "purple": return Make("purple", "#EEEAF7", "#FAF9FD", "#FFFFFF", "#E2DCF1", "#D9D3E8", "#272036", "#463C60", "#645A80", "#7A7193", "#D3CBE9", "#4F4470", "#6A4BD6", "#FFFFFF");
                case "peach": return Make("peach", "#FBE9E4", "#FFF9F7", "#FFFFFF", "#F6DCD5", "#EFD3CC", "#3A2623", "#5E423D", "#7D5C56", "#8F706A", "#F2CFC6", "#74514A", "#C9365B", "#FFFFFF");
                case "mono": return Make("mono", "#F1F1F1", "#FFFFFF", "#FFFFFF", "#E6E6E6", "#DADADA", "#1A1A1A", "#3D3D3D", "#5E5E5E", "#757575", "#DDDDDD", "#4A4A4A", "#2B2B2B", "#FFFFFF");
                default: return Make("default", "#BACDDC", "#FCFCF8", "#FFFFFF", "#D3E0E9", "#E3E9ED", "#293B46", "#394754", "#718795", "#84909A", "#A7BFCE", "#496373", "#FFE36B", "#293B46");
            }
        }

        private static Theme Make(string id, string window, string panel, string bubble, string soft, string border,
            string text, string mid, string sub, string faint, string pill, string pillText, string accent, string accentText)
        {
            return new Theme
            {
                Id = id, Window = Rgb.FromHex(window), Panel = Rgb.FromHex(panel), OtherBubble = Rgb.FromHex(bubble),
                OtherText = Rgb.FromHex(text), Soft = Rgb.FromHex(soft), Border = Rgb.FromHex(border), Text = Rgb.FromHex(text),
                MidText = Rgb.FromHex(mid), SubText = Rgb.FromHex(sub), FaintText = Rgb.FromHex(faint), DatePill = Rgb.FromHex(pill),
                // User revision: the peach own bubble uses the soft surface, while controls keep the reference accent.
                Notice = Rgb.FromHex(pillText), MineBubble = Rgb.FromHex(id == "peach" ? soft : accent), MineText = Rgb.FromHex(id == "peach" ? text : accentText),
                Accent = Rgb.FromHex(accent), AccentText = Rgb.FromHex(accentText)
            };
        }

        /// <summary>Foreground/background pairs used for contrast measurements; the approved legacy light palette keeps its original secondary colours.</summary>
        public IEnumerable<KeyValuePair<string, Rgb[]>> TextPairs()
        {
            yield return new KeyValuePair<string, Rgb[]>("text/window", new[] { Text, Window });
            yield return new KeyValuePair<string, Rgb[]>("subtext/window", new[] { SubText, Window });
            yield return new KeyValuePair<string, Rgb[]>("text/panel", new[] { Text, Panel });
            yield return new KeyValuePair<string, Rgb[]>("subtext/panel", new[] { SubText, Panel });
            yield return new KeyValuePair<string, Rgb[]>("other", new[] { OtherText, OtherBubble });
            yield return new KeyValuePair<string, Rgb[]>("mine", new[] { MineText, MineBubble });
            yield return new KeyValuePair<string, Rgb[]>("accent", new[] { AccentText, Accent });
            yield return new KeyValuePair<string, Rgb[]>("notice/pill", new[] { Notice, DatePill });
            yield return new KeyValuePair<string, Rgb[]>("warning/window", new[] { Warning, Window });
            yield return new KeyValuePair<string, Rgb[]>("warning/panel", new[] { Warning, Panel });
        }
    }
}
