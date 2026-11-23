using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Domain.Appearance
{
    /// <summary>A cat look: skin + hat identifiers as read by the game component (70-SPEC 2.2).</summary>
    public sealed class AppearanceKey : IEquatable<AppearanceKey>
    {
        public readonly string Skin;
        public readonly string Hat;
        public AppearanceKey(string skin, string hat) { Skin = skin ?? ""; Hat = hat ?? ""; }
        public string Id { get { return Skin + "|" + Hat; } }
        public bool Equals(AppearanceKey o) { return o != null && o.Skin == Skin && o.Hat == Hat; }
        public override bool Equals(object obj) { return Equals(obj as AppearanceKey); }
        public override int GetHashCode() { return Id.GetHashCode(); }
    }

    public sealed class PortraitEntry
    {
        public AppearanceKey Key;
        public string File;          // path inside the temporary cache (adapter decides)
        public DateTime CreatedUtc;
    }

    /// <summary>
    /// Policy for cat portraits: one render per look, keep the last good image on failure, delete pictures of people
    /// who left (30 s grace for images being replaced), and wipe everything when the feature or the app turns off.
    /// File I/O happens in the adapter; this class only decides.
    /// </summary>
    public sealed class PortraitCache
    {
        public static readonly TimeSpan ReplaceGrace = TimeSpan.FromSeconds(30);
        private readonly Dictionary<AppearanceKey, PortraitEntry> _byKey = new Dictionary<AppearanceKey, PortraitEntry>();
        private readonly Dictionary<string, AppearanceKey> _person = new Dictionary<string, AppearanceKey>(StringComparer.Ordinal);
        private readonly Dictionary<string, PortraitEntry> _lastGood = new Dictionary<string, PortraitEntry>(StringComparer.Ordinal);
        private readonly HashSet<AppearanceKey> _requested = new HashSet<AppearanceKey>();
        private readonly List<KeyValuePair<DateTime, PortraitEntry>> _retired = new List<KeyValuePair<DateTime, PortraitEntry>>();

        /// <summary>Records a person's current look. Returns true if a new render is needed for that look.</summary>
        public bool SetLook(string steamId, AppearanceKey key, DateTime nowUtc)
        {
            AppearanceKey old;
            if (_person.TryGetValue(steamId, out old) && !old.Equals(key)) RetireIfUnused(old, steamId, nowUtc);
            _person[steamId] = key;
            if (_byKey.ContainsKey(key) || _requested.Contains(key)) return false;
            _requested.Add(key);
            return true;
        }

        public void OnRendered(AppearanceKey key, string file, DateTime nowUtc)
        {
            _requested.Remove(key);
            var e = new PortraitEntry { Key = key, File = file, CreatedUtc = nowUtc };
            _byKey[key] = e;
            foreach (var kv in _person.Where(p => p.Value.Equals(key)).ToList()) _lastGood[kv.Key] = e;
        }

        /// <summary>Render failure keeps whatever the person had before.</summary>
        public void OnRenderFailed(AppearanceKey key) { _requested.Remove(key); }

        /// <summary>Picture to draw, or null → initial-letter circle.</summary>
        public PortraitEntry For(string steamId)
        {
            AppearanceKey k;
            PortraitEntry e;
            if (_person.TryGetValue(steamId, out k) && _byKey.TryGetValue(k, out e)) return e;
            return _lastGood.TryGetValue(steamId, out e) ? e : null;
        }

        private void RetireIfUnused(AppearanceKey key, string exceptPerson, DateTime nowUtc)
        {
            bool used = _person.Any(p => p.Key != exceptPerson && p.Value.Equals(key));
            PortraitEntry e;
            if (!used && _byKey.TryGetValue(key, out e)) _retired.Add(new KeyValuePair<DateTime, PortraitEntry>(nowUtc, e));
        }

        /// <summary>Leaving a room: everyone's pictures go (returns files to delete now).</summary>
        public List<string> LeaveRoom()
        {
            var files = _byKey.Values.Select(e => e.File).Concat(_retired.Select(r => r.Value.File)).Distinct().ToList();
            _byKey.Clear(); _person.Clear(); _requested.Clear(); _lastGood.Clear(); _retired.Clear();
            return files;
        }

        public List<string> Clear() { return LeaveRoom(); }

        /// <summary>Files replaced more than 30 s ago that nobody uses any more.</summary>
        public List<string> Sweep(DateTime nowUtc)
        {
            var due = _retired.Where(r => nowUtc - r.Key >= ReplaceGrace).ToList();
            var files = new List<string>();
            foreach (var r in due)
            {
                _retired.Remove(r);
                bool stillUsed = _person.Values.Any(k => k.Equals(r.Value.Key)) || _lastGood.Values.Any(v => v == r.Value);
                if (stillUsed) continue;
                _byKey.Remove(r.Value.Key);
                files.Add(r.Value.File);
            }
            return files;
        }

        public int RenderedCount { get { return _byKey.Count; } }
    }
}
