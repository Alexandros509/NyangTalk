using System;
using System.Collections.Generic;

namespace NyangTalk.Domain.People
{
    public enum DisplayNameMode { AliasOnly = 0, AliasThenOriginal = 1, OriginalThenAlias = 2 }

    public static class DisplayName
    {
        public static string Format(string original, string alias, DisplayNameMode mode)
        {
            if (string.IsNullOrEmpty(alias)) return original ?? "";
            switch (mode)
            {
                case DisplayNameMode.AliasOnly: return alias;
                case DisplayNameMode.AliasThenOriginal: return alias + " (" + original + ")";
                default: return original + " (" + alias + ")";
            }
        }

        /// <summary>First letter for the circle placeholder (surrogate-pair safe).</summary>
        public static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            name = name.Trim();
            if (name.Length == 0) return "?";
            if (char.IsHighSurrogate(name[0]) && name.Length > 1) return name.Substring(0, 2);
            return name.Substring(0, 1).ToUpperInvariant();
        }
    }

    public enum AliasError { None, Empty, TooLong, HasNewline }

    /// <summary>Aliases keyed by Steam ID so a rename keeps the alias (60-SPEC 2장).</summary>
    public sealed class AliasBook
    {
        public const int MaxLength = 20;
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

        public static AliasError Validate(string raw, out string cleaned)
        {
            cleaned = (raw ?? "").Trim();
            if (cleaned.IndexOf('\n') >= 0 || cleaned.IndexOf('\r') >= 0) return AliasError.HasNewline;
            if (cleaned.Length == 0) return AliasError.Empty;
            if (CountChars(cleaned) > MaxLength) return AliasError.TooLong;
            return AliasError.None;
        }

        /// <summary>Counts user-perceived characters roughly: surrogate pairs count as one.</summary>
        public static int CountChars(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++) { if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++; n++; }
            return n;
        }

        public AliasError Set(string steamId, string raw)
        {
            string cleaned;
            var e = Validate(raw, out cleaned);
            if (e != AliasError.None) return e;
            _map[steamId] = cleaned;
            return AliasError.None;
        }

        public void Remove(string steamId) { _map.Remove(steamId); }
        public string Get(string steamId)
        {
            string a;
            return steamId != null && _map.TryGetValue(steamId, out a) ? a : null;
        }
        public IDictionary<string, string> All { get { return _map; } }
        public void Load(IDictionary<string, string> data)
        {
            _map.Clear();
            foreach (var kv in data) { string c; if (Validate(kv.Value, out c) == AliasError.None) _map[kv.Key] = c; }
        }
    }

    public enum PersonColorMode { None = 0, Auto = 1, Manual = 2 }
    public enum ColorPlacement { NameText = 0, MessageBackground = 1, Both = 2 }

    public struct Rgb
    {
        public byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public static Rgb FromHex(string hex)
        {
            hex = hex.TrimStart('#');
            return new Rgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
        }
        public string ToHex() { return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2"); }
        public override string ToString() { return ToHex(); }
    }

    /// <summary>WCAG 2.x relative luminance and contrast ratio.</summary>
    public static class ColorMath
    {
        public const double MinContrast = 4.5;

        private static double Channel(byte c)
        {
            double s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Rgb c) { return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B); }

        public static double Contrast(Rgb a, Rgb b)
        {
            double la = Luminance(a), lb = Luminance(b);
            if (la < lb) { var t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        /// <summary>Linear mix in sRGB space: t=0 → a, t=1 → b.</summary>
        public static Rgb Mix(Rgb a, Rgb b, double t)
        {
            return new Rgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Moves <paramref name="fg"/> toward black or white (whichever gives more contrast with <paramref name="bg"/>)
        /// until contrast ≥ 4.5. Keeps the hue as much as possible.
        /// </summary>
        public static Rgb EnsureTextContrast(Rgb fg, Rgb bg)
        {
            if (Contrast(fg, bg) >= MinContrast) return fg;
            var black = new Rgb(0, 0, 0); var white = new Rgb(255, 255, 255);
            var target = Contrast(black, bg) >= Contrast(white, bg) ? black : white;
            for (int i = 1; i <= 40; i++)
            {
                var c = Mix(fg, target, i / 40.0);
                if (Contrast(c, bg) >= MinContrast) return c;
            }
            return target;
        }

        /// <summary>
        /// Light tint of the person colour on the theme background such that the body text keeps ≥ 4.5:1.
        /// Starts at 22% strength and lowers it until the contrast holds (60-SPEC 3.2).
        /// </summary>
        public static Rgb Tint(Rgb person, Rgb themeBubble, Rgb bodyText)
        {
            for (int pct = 22; pct >= 0; pct -= 2)
            {
                var c = Mix(themeBubble, person, pct / 100.0);
                if (Contrast(c, bodyText) >= MinContrast) return c;
            }
            return themeBubble;
        }
    }

    /// <summary>Per-person colours (60-SPEC 3장). Auto colour is a fixed function of the Steam ID.</summary>
    public sealed class PersonColors
    {
        /// <summary>Palette for automatic and manual colours (own selection; hues spread around the wheel).</summary>
        public static readonly Rgb[] Palette = new[]
        {
            Rgb.FromHex("#E5484D"), Rgb.FromHex("#F76B15"), Rgb.FromHex("#D6A100"), Rgb.FromHex("#30A46C"),
            Rgb.FromHex("#12A594"), Rgb.FromHex("#0090FF"), Rgb.FromHex("#3E63DD"), Rgb.FromHex("#8E4EC6"),
            Rgb.FromHex("#D6409F"), Rgb.FromHex("#978365"), Rgb.FromHex("#5B8A3C"), Rgb.FromHex("#C2298A")
        };

        public PersonColorMode Mode = PersonColorMode.Auto;
        public ColorPlacement Placement = ColorPlacement.NameText;
        private readonly Dictionary<string, int> _manual = new Dictionary<string, int>(StringComparer.Ordinal);

        public IDictionary<string, int> Manual { get { return _manual; } }

        /// <summary>Placement only matters when colours are on (60-SPEC 3.2: disabled for "색 없음").</summary>
        public bool PlacementEnabled { get { return Mode != PersonColorMode.None; } }

        public static int AutoIndex(string steamId)
        {
            // FNV-1a 32-bit over the UTF-16 code units: stable across runs and machines.
            uint h = 2166136261;
            foreach (var ch in steamId ?? "") { h ^= ch; h *= 16777619; }
            return (int)(h % (uint)Palette.Length);
        }

        /// <summary>Base colour for a person, or null when colouring is off or the message is mine.</summary>
        public Rgb? BaseColor(string steamId, bool isMine)
        {
            if (Mode == PersonColorMode.None || isMine) return null;
            int idx;
            if (Mode == PersonColorMode.Manual && steamId != null && _manual.TryGetValue(steamId, out idx) && idx >= 0 && idx < Palette.Length)
                return Palette[idx];
            return Palette[AutoIndex(steamId)];
        }

        public bool ColorsName { get { return Mode != PersonColorMode.None && (Placement == ColorPlacement.NameText || Placement == ColorPlacement.Both); } }
        public bool ColorsBackground { get { return Mode != PersonColorMode.None && (Placement == ColorPlacement.MessageBackground || Placement == ColorPlacement.Both); } }
    }
}
