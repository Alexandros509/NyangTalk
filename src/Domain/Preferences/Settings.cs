using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NyangTalk.Domain.Preferences
{
    public sealed class SettingDef
    {
        public string Key;
        public string Default;
        /// <summary>Allowed values; null means free text; "int:min:max" style handled by Min/Max.</summary>
        public string[] Choices;
        public int? Min, Max;
        public int Step = 1;
        public bool RestartRequired;
    }

    /// <summary>
    /// All user-visible settings (90-SPEC 1장) as validated key/value pairs. Unknown keys from newer files are kept
    /// untouched on save (MINOR-compatible data format).
    /// </summary>
    public sealed class Settings
    {
        public static readonly List<SettingDef> Defs = new List<SettingDef>
        {
            C("screen.windowSize", "default", WindowSizes.Choices),
            C("screen.layout", "auto", "auto", "single", "sidebar"),
            C("screen.preset", "basic", "basic", "mobile", "business", "wire"),
            C("screen.theme", "default", Theme.Ids),
            C("screen.dark", "false", "false", "true"),
            C("screen.chatShape", "bubble", "bubble", "card", "compact"),
            C("screen.headerButtons", "icon", "icon", "pill"),
            C("screen.font", "pretendard", "pretendard", "notosanskr", "suit", "malgun"),
            I("screen.fontSize", 11, 8, 16),
            C("screen.border", "light", "none", "light", "normal", "strong"),
            C("screen.topmost", "false", "false", "true"),
            new SettingDef { Key = "screen.opacity", Default = "100", Min = 40, Max = 100, Step = 5 },
            C("screen.opacityHover", "false", "false", "true"),
            I("screen.sidebarWidth", 240, 200, 320),
            C("screen.sidebarCollapsed", "false", "false", "true"),
            C("screen.tailBubble", "true", "false", "true"),
            C("chat.emoteDisplay", "image", "image", "name"),
            C("chat.emoteMode", "A1", "A1", "A2", "B", "C"),
            C("chat.emoteWindow", "15", "15", "30", "180"),
            C("chat.hideMeows", "false", "false", "true"),
            C("chat.clock", "24", "24", "12"),
            C("chat.retentionDays", "30", "7", "30", "90", "365", "0"),
            C("chat.searchIgnoreCase", "true", "false", "true"),
            C("chat.searchIgnoreWidth", "true", "false", "true"),
            C("people.nameMode", "aliasOriginal", "alias", "aliasOriginal", "originalAlias"),
            C("people.colorMode", "auto", "none", "auto", "manual"),
            C("people.colorPlacement", "name", "name", "background", "both"),
            C("notify.mode", "flashBadge", "off", "flash", "flashBadge"),
            C("char.show", "true", "false", "true"),
            C("char.size", "normal", "small", "normal", "large"),
            C("char.compact", "false", "false", "true"),
            C("char.shape", "original", "original", "circle"),
            C("char.scope", "hat", "face", "hat"),
            C("char.continuation", "every", "every", "first"),
            C("char.mine", "hide", "show", "hide"),
            C("char.minePosition", "left", "left", "right"),
            C("auto.chest", "false", "false", "true"),
            C("auto.chestTypes", "both", "both", "cosmetic", "emote"),
            I("auto.chestDelay", 3, 0, 60),
            I("auto.chestMaxPerHour", 20, 1, 120),
            C("auto.gifts", "false", "false", "true"),
            new SettingDef { Key = "link.gameDir", Default = "" },
            C("link.startWithGame", "false", "false", "true"),
            C("update.mode", "auto", "auto", "notifyOnly"),
            C("update.channel", "stable", "stable", "beta"),
            C("ui.language", "ko", "ko", "en", "ja"),
        };

        private static SettingDef C(string key, string def, params string[] choices) { return new SettingDef { Key = key, Default = def, Choices = choices }; }
        private static SettingDef I(string key, int def, int min, int max) { return new SettingDef { Key = key, Default = def.ToString(CultureInfo.InvariantCulture), Min = min, Max = max }; }

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _unknown = new Dictionary<string, string>(StringComparer.Ordinal);

        public event Action<string> Changed;

        public static SettingDef Def(string key) { return Defs.FirstOrDefault(d => d.Key == key); }

        public string Get(string key)
        {
            string v;
            if (_values.TryGetValue(key, out v)) return v;
            var d = Def(key);
            if (d == null) throw new ArgumentException("unknown setting " + key);
            return d.Default;
        }

        public bool GetBool(string key) { return Get(key) == "true"; }
        public int GetInt(string key) { return int.Parse(Get(key), CultureInfo.InvariantCulture); }

        public static bool IsValid(SettingDef d, string value)
        {
            if (value == null) return false;
            if (d.Choices != null) return d.Choices.Contains(value);
            if (d.Min.HasValue)
            {
                int n;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
                return n >= d.Min.Value && n <= d.Max.Value && (n - d.Min.Value) % d.Step == 0;
            }
            return true;
        }

        public bool Set(string key, string value)
        {
            var d = Def(key);
            if (d == null || !IsValid(d, value)) return false;
            if (Get(key) == value) return true;
            _values[key] = value;
            var h = Changed;
            if (h != null) h(key);
            return true;
        }

        public void Set(string key, bool value) { Set(key, value ? "true" : "false"); }
        public void Set(string key, int value) { Set(key, value.ToString(CultureInfo.InvariantCulture)); }

        /// <summary>Loads stored values; invalid values fall back to defaults, unknown keys are preserved.</summary>
        public void Load(IDictionary<string, string> stored)
        {
            _values.Clear(); _unknown.Clear();
            foreach (var kv in stored)
            {
                var d = Def(kv.Key);
                if (d == null) { _unknown[kv.Key] = kv.Value; continue; }
                if (IsValid(d, kv.Value)) _values[kv.Key] = kv.Value;
            }
        }

        public Dictionary<string, string> Snapshot()
        {
            var r = new Dictionary<string, string>(_unknown, StringComparer.Ordinal);
            foreach (var d in Defs) r[d.Key] = Get(d.Key);
            return r;
        }
    }

    /// <summary>
    /// Chat window size steps (90-SPEC 1.5) in logical pixels, before display scaling. "custom" marks a size the user
    /// dragged; it is never applied, only shown.
    /// </summary>
    public static class WindowSizes
    {
        public const string Custom = "custom";
        public static readonly string[] Steps = { "mini", "small", "default", "large" };
        public static readonly string[] Choices = { "mini", "small", "default", "large", Custom };

        /// <summary>Logical width and height of a step, or null for "custom"/unknown.</summary>
        public static int[] Logical(string step)
        {
            switch (step)
            {
                case "mini": return new[] { 300, 360 };
                case "small": return new[] { 380, 520 };
                case "default": return new[] { 510, 790 };
                case "large": return new[] { 680, 900 };
                default: return null;
            }
        }

        /// <summary>Device pixels for a display DPI (96 = 100%, 120 = 125%, 144 = 150%).</summary>
        public static int[] Pixels(string step, int dpi)
        {
            var l = Logical(step);
            if (l == null) return null;
            return new[] { Scale(l[0], dpi), Scale(l[1], dpi) };
        }

        public static int Scale(int logical, int dpi) { return (int)Math.Round(logical * (dpi <= 0 ? 96 : dpi) / 96.0); }
    }

    public enum LayoutKind { Single, Sidebar }

    /// <summary>Layout choice with hysteresis (30-SPEC 2장): ≥760 → sidebar, &lt;720 → single, in between keep.</summary>
    public static class LayoutResolver
    {
        public const int SidebarAt = 760;
        public const int SingleBelow = 720;

        public static LayoutKind Resolve(string setting, int width, LayoutKind current)
        {
            if (setting == "single") return LayoutKind.Single;
            if (setting == "sidebar") return LayoutKind.Sidebar;
            if (width >= SidebarAt) return LayoutKind.Sidebar;
            if (width < SingleBelow) return LayoutKind.Single;
            return current;
        }

        /// <summary>Fixed sidebar layout on a window narrower than 720 collapses the sidebar (30-SPEC 6장).</summary>
        public static bool ForceCollapse(string setting, int width) { return setting == "sidebar" && width < SingleBelow; }
    }

    public static class RetentionPolicy
    {
        /// <summary>0 means keep forever.</summary>
        public static bool IsExpired(DateTime lastMessageUtc, DateTime nowUtc, int days)
        {
            if (days <= 0) return false;
            return nowUtc - lastMessageUtc > TimeSpan.FromDays(days);
        }
    }

    /// <summary>Diagnostics text must not carry message bodies, names or full Steam IDs (90-SPEC 5장).</summary>
    public static class DiagnosticsSanitizer
    {
        private static readonly Regex SteamId = new Regex(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant);

        public static string MaskSteamIds(string s)
        {
            return SteamId.Replace(s ?? "", m => "…" + m.Value.Substring(m.Value.Length - 4));
        }

        public static string MaskUserPath(string s, string userProfile)
        {
            if (string.IsNullOrEmpty(userProfile) || s == null) return s;
            return Regex.Replace(s, Regex.Escape(userProfile), "%USERPROFILE%", RegexOptions.IgnoreCase);
        }

        public static string Line(string s, string userProfile) { return MaskUserPath(MaskSteamIds(s), userProfile); }

        public const string Hidden = "[가림]";

        /// <summary>
        /// Replaces chat texts and names the app knows (95-SPEC 2.5) with [가림], longest first. Words shorter than two
        /// characters are left alone: a one-letter "name" would blank every line.
        /// </summary>
        public static string MaskWords(string s, IEnumerable<string> words)
        {
            if (string.IsNullOrEmpty(s) || words == null) return s;
            foreach (var w in words.Where(x => x != null && x.Trim().Length >= 2).Select(x => x.Trim()).Distinct().OrderByDescending(x => x.Length))
                if (s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                    s = Regex.Replace(s, Regex.Escape(w), Hidden, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return s;
        }

        /// <summary>One log line: user folder, then known chat texts and names, then full Steam IDs.</summary>
        public static string LogLine(string s, string userProfile, IEnumerable<string> words)
        {
            return MaskSteamIds(MaskWords(MaskUserPath(s ?? "", userProfile), words));
        }
    }
}
