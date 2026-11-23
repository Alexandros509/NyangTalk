using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NyangTalk.Domain.Update
{
    /// <summary>
    /// Semantic Versioning 2.0.0 (https://semver.org/). The only place in the product that parses or compares versions
    /// (01-OVERVIEW 2.2). Build metadata is kept for display but ignored for precedence and equality.
    /// </summary>
    public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
    {
        // Official grammar from semver.org (numeric identifiers without leading zeros).
        private static readonly Regex Grammar = new Regex(
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)" +
            @"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?" +
            @"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$", RegexOptions.CultureInvariant);

        private readonly int _major, _minor, _patch;
        private readonly string[] _pre;
        private readonly string _build;

        private SemVersion(int major, int minor, int patch, string[] pre, string build)
        {
            _major = major; _minor = minor; _patch = patch; _pre = pre; _build = build;
        }

        public int Major { get { return _major; } }
        public int Minor { get { return _minor; } }
        public int Patch { get { return _patch; } }
        public bool IsPrerelease { get { return _pre.Length > 0; } }
        public string Prerelease { get { return string.Join(".", _pre); } }
        public string Build { get { return _build; } }

        public static bool TryParse(string text, out SemVersion version)
        {
            version = null;
            if (text == null) return false;
            var m = Grammar.Match(text);
            if (!m.Success) return false;
            int major, minor, patch;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)) return false;
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
            if (!int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            var pre = m.Groups[4].Success ? m.Groups[4].Value.Split('.') : new string[0];
            var build = m.Groups[5].Success ? m.Groups[5].Value : "";
            version = new SemVersion(major, minor, patch, pre, build);
            return true;
        }

        public static SemVersion Parse(string text)
        {
            SemVersion v;
            if (!TryParse(text, out v)) throw new FormatException("not a SemVer 2.0.0 version: " + text);
            return v;
        }

        public int CompareTo(SemVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            int c = _major.CompareTo(other._major); if (c != 0) return c;
            c = _minor.CompareTo(other._minor); if (c != 0) return c;
            c = _patch.CompareTo(other._patch); if (c != 0) return c;
            // A version without pre-release has higher precedence.
            if (_pre.Length == 0 && other._pre.Length == 0) return 0;
            if (_pre.Length == 0) return 1;
            if (other._pre.Length == 0) return -1;
            int n = Math.Min(_pre.Length, other._pre.Length);
            for (int i = 0; i < n; i++)
            {
                c = CompareIdentifier(_pre[i], other._pre[i]);
                if (c != 0) return c;
            }
            return _pre.Length.CompareTo(other._pre.Length);
        }

        private static int CompareIdentifier(string a, string b)
        {
            bool an = IsNumeric(a), bn = IsNumeric(b);
            if (an && bn)
            {
                // Compare as numbers without overflow: longer digit string is larger (no leading zeros allowed).
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                return string.CompareOrdinal(a, b) < 0 ? -1 : (string.CompareOrdinal(a, b) > 0 ? 1 : 0);
            }
            if (an) return -1; // numeric identifiers have lower precedence than alphanumeric
            if (bn) return 1;
            int c = string.CompareOrdinal(a, b);
            return c < 0 ? -1 : (c > 0 ? 1 : 0);
        }

        private static bool IsNumeric(string s)
        {
            if (s.Length == 0) return false;
            foreach (char ch in s) if (ch < '0' || ch > '9') return false;
            return true;
        }

        public bool Equals(SemVersion other) { return CompareTo(other) == 0; }
        public override bool Equals(object obj) { return Equals(obj as SemVersion); }
        public override int GetHashCode()
        {
            int h = _major * 397 ^ _minor * 31 ^ _patch;
            foreach (var p in _pre) h = h * 31 + p.GetHashCode();
            return h;
        }

        public static bool operator <(SemVersion a, SemVersion b) { return Compare(a, b) < 0; }
        public static bool operator >(SemVersion a, SemVersion b) { return Compare(a, b) > 0; }
        public static bool operator <=(SemVersion a, SemVersion b) { return Compare(a, b) <= 0; }
        public static bool operator >=(SemVersion a, SemVersion b) { return Compare(a, b) >= 0; }
        public static bool operator ==(SemVersion a, SemVersion b) { return Compare(a, b) == 0; }
        public static bool operator !=(SemVersion a, SemVersion b) { return Compare(a, b) != 0; }

        private static int Compare(SemVersion a, SemVersion b)
        {
            if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) ? 0 : -1;
            return a.CompareTo(b);
        }

        /// <summary>Windows file version (numeric 4 parts): MAJOR.MINOR.PATCH.0</summary>
        public string ToFileVersion() { return _major + "." + _minor + "." + _patch + ".0"; }

        public override string ToString()
        {
            var s = _major + "." + _minor + "." + _patch;
            if (_pre.Length > 0) s += "-" + Prerelease;
            if (_build.Length > 0) s += "+" + _build;
            return s;
        }
    }

    /// <summary>Space-separated comparator set, e.g. "&gt;=1.2.0 &lt;2.0.0". All comparators must hold.</summary>
    public sealed class VersionRange
    {
        private readonly List<KeyValuePair<string, SemVersion>> _parts;
        private readonly string _text;

        private VersionRange(string text, List<KeyValuePair<string, SemVersion>> parts) { _text = text; _parts = parts; }

        public static bool TryParse(string text, out VersionRange range)
        {
            range = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = new List<KeyValuePair<string, SemVersion>>();
            foreach (var raw in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string op = null;
                foreach (var candidate in new[] { ">=", "<=", ">", "<", "=" })
                {
                    if (raw.StartsWith(candidate, StringComparison.Ordinal)) { op = candidate; break; }
                }
                if (op == null) return false;
                SemVersion v;
                if (!SemVersion.TryParse(raw.Substring(op.Length), out v)) return false;
                parts.Add(new KeyValuePair<string, SemVersion>(op, v));
            }
            if (parts.Count == 0) return false;
            range = new VersionRange(text, parts);
            return true;
        }

        public static VersionRange Parse(string text)
        {
            VersionRange r;
            if (!TryParse(text, out r)) throw new FormatException("bad version range: " + text);
            return r;
        }

        public bool Contains(SemVersion v)
        {
            foreach (var p in _parts)
            {
                int c = v.CompareTo(p.Value);
                switch (p.Key)
                {
                    case ">=": if (c < 0) return false; break;
                    case "<=": if (c > 0) return false; break;
                    case ">": if (c <= 0) return false; break;
                    case "<": if (c >= 0) return false; break;
                    case "=": if (c != 0) return false; break;
                }
            }
            return true;
        }

        public override string ToString() { return _text; }
    }
}
