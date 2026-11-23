using System;
using System.Collections.Generic;
using System.Linq;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.Application
{
    /// <summary>
    /// Loads and saves settings, aliases and person colours. Changes apply immediately; when saving fails the change
    /// stays applied and <see cref="SaveFailed"/> fires (90-SPEC 1장, 60-SPEC 2장).
    /// </summary>
    public sealed class PreferencesService
    {
        public const string SettingsDoc = "settings";
        public const string AliasesDoc = "aliases";
        public const string ColorsDoc = "person-colors";
        public const string DataFormat = "1.0.0";

        private readonly IDocumentStore _store;
        public readonly Settings Settings = new Settings();
        public readonly AliasBook Aliases = new AliasBook();
        public readonly PersonColors Colors = new PersonColors();
        public string BrokenSettingsCopy { get; private set; }
        /// <summary>True on the very first run when no settings file exists yet (95-SPEC 2.9).</summary>
        public bool FirstRun { get; private set; }

        public event Action<string> SaveFailed;   // argument: localisation key of the message
        public event Action Changed;

        public PreferencesService(IDocumentStore store) { _store = store; }

        /// <summary>Loads everything. The language defaults to Korean whatever the Windows language is (90-SPEC 3장).</summary>
        public void Load()
        {
            FirstRun = !_store.Exists(SettingsDoc) && !_store.HasBrokenCopy(SettingsDoc);
            LoadSettingsValues();
            if (BrokenSettingsCopy != null) FirstRun = false;
            string broken;
            var a = _store.Load(AliasesDoc, out broken);
            var amap = new Dictionary<string, string>();
            if (a != null)
            {
                object v;
                if (a.TryGetValue("aliases", out v) && v is IDictionary<string, object>)
                    foreach (var kv in (IDictionary<string, object>)v) if (kv.Value is string) amap[kv.Key] = (string)kv.Value;
            }
            Aliases.Load(amap);

            var c = _store.Load(ColorsDoc, out broken);
            Colors.Manual.Clear();
            if (c != null)
            {
                object v;
                if (c.TryGetValue("manual", out v) && v is IDictionary<string, object>)
                    foreach (var kv in (IDictionary<string, object>)v) if (kv.Value is long) Colors.Manual[kv.Key] = (int)(long)kv.Value;
            }
            ApplyColorSettings();
            Settings.Changed += k => { ApplyColorSettings(); SaveSettings(); var h = Changed; if (h != null) h(); };
        }

        private void LoadSettingsValues()
        {
            string broken;
            var s = _store.Load(SettingsDoc, out broken);
            if (broken != null) BrokenSettingsCopy = broken;
            var values = new Dictionary<string, string>();
            if (s != null)
            {
                object v;
                if (s.TryGetValue("values", out v) && v is IDictionary<string, object>)
                    foreach (var kv in (IDictionary<string, object>)v) if (kv.Value is string) values[kv.Key] = (string)kv.Value;
            }
            Settings.Load(values);
        }

        /// <summary>
        /// "다시 불러오기" after the settings file was found broken (90-SPEC 1.4): reads the file again (the user may have
        /// put a fixed copy back) and tells every screen. Returns false when the file is still unreadable.
        /// </summary>
        public bool ReloadSettings()
        {
            // the broken file was moved aside at load; with no file there is nothing to reload (still defaults)
            if (!_store.Exists(SettingsDoc)) return false;
            BrokenSettingsCopy = null;
            LoadSettingsValues();
            ApplyColorSettings();
            var h = Changed; if (h != null) h();
            return BrokenSettingsCopy == null;
        }

        private void ApplyColorSettings()
        {
            switch (Settings.Get("people.colorMode")) { case "none": Colors.Mode = PersonColorMode.None; break; case "manual": Colors.Mode = PersonColorMode.Manual; break; default: Colors.Mode = PersonColorMode.Auto; break; }
            switch (Settings.Get("people.colorPlacement")) { case "background": Colors.Placement = ColorPlacement.MessageBackground; break; case "both": Colors.Placement = ColorPlacement.Both; break; default: Colors.Placement = ColorPlacement.NameText; break; }
        }

        public DisplayNameMode NameMode
        {
            get
            {
                switch (Settings.Get("people.nameMode")) { case "alias": return DisplayNameMode.AliasOnly; case "originalAlias": return DisplayNameMode.OriginalThenAlias; default: return DisplayNameMode.AliasThenOriginal; }
            }
        }

        public string Lang { get { return Settings.Get("ui.language"); } }
        public string L(string key) { return Strings.Get(key, Lang); }

        public bool SaveSettings()
        {
            var doc = new Dictionary<string, object> { { "format", DataFormat }, { "values", Settings.Snapshot().ToDictionary(kv => kv.Key, kv => (object)kv.Value) } };
            bool ok = Report(_store.Save(SettingsDoc, doc), "settings.saveFailed");
            if (ok) FirstRun = false;
            return ok;
        }

        /// <summary>95-SPEC 2.9: saves the language chosen on the first-run screen and marks first-run complete.</summary>
        public void CompleteFirstRun(string lang)
        {
            FirstRun = false;
            if (!Settings.Set("ui.language", lang)) SaveSettings();
            var h = Changed; if (h != null) h();
        }

        /// <summary>Sets an alias; the screen shows it even if saving fails.</summary>
        public AliasError SetAlias(string steamId, string raw)
        {
            var e = Aliases.Set(steamId, raw);
            if (e != AliasError.None) return e;
            SaveAliases();
            var h = Changed; if (h != null) h();
            return e;
        }

        public void RemoveAlias(string steamId) { Aliases.Remove(steamId); SaveAliases(); var h = Changed; if (h != null) h(); }

        private void SaveAliases()
        {
            var doc = new Dictionary<string, object> { { "format", DataFormat }, { "aliases", Aliases.All.ToDictionary(kv => kv.Key, kv => (object)kv.Value) } };
            Report(_store.Save(AliasesDoc, doc), "people.saveFailed");
        }

        public void SetManualColor(string steamId, int paletteIndex)
        {
            Colors.Manual[steamId] = paletteIndex;
            var doc = new Dictionary<string, object> { { "format", DataFormat }, { "manual", Colors.Manual.ToDictionary(kv => kv.Key, kv => (object)(long)kv.Value) } };
            Report(_store.Save(ColorsDoc, doc), "people.saveFailed");
            var h = Changed; if (h != null) h();
        }

        public const string WindowsDoc = "window-sizes";

        /// <summary>Remembered outer size of a window ("settings", or "main" after the user dragged it), if any.</summary>
        public int[] LoadWindowSize(string name)
        {
            string broken;
            var d = _store.Load(WindowsDoc, out broken);
            object v;
            if (d == null || !d.TryGetValue(name, out v) || !(v is IDictionary<string, object>)) return null;
            var m = (IDictionary<string, object>)v;
            object w, h;
            if (!m.TryGetValue("w", out w) || !m.TryGetValue("h", out h) || !(w is long) || !(h is long)) return null;
            long lw = (long)w, lh = (long)h;
            if (lw < 100 || lh < 100 || lw > 20000 || lh > 20000) return null;
            return new[] { (int)lw, (int)lh };
        }

        public void SaveWindowSize(string name, int width, int height)
        {
            string broken;
            var d = _store.Load(WindowsDoc, out broken);
            var doc = d == null ? new Dictionary<string, object>() : new Dictionary<string, object>(d);
            doc[name] = new Dictionary<string, object> { { "w", (long)width }, { "h", (long)height } };
            Report(_store.Save(WindowsDoc, doc), "settings.saveFailed");
        }

        private bool Report(bool ok, string key)
        {
            if (!ok) { var h = SaveFailed; if (h != null) h(key); }
            return ok;
        }
    }
}
