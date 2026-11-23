using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>Everything the locator reads from the PC; replaceable in tests (and counts file accesses).</summary>
    public interface ILocatorEnv
    {
        string RunningGameExe();                       // path of a running BongoCat.exe, or null
        string Registry(string hive, string key, string name);
        bool FileExists(string path);
        string ReadText(string path);                  // null if missing
        string FinalPath(string dir);                  // resolves links; returns dir if it cannot
    }

    public sealed class RealLocatorEnv : ILocatorEnv
    {
        public int FileChecks;

        public string RunningGameExe()
        {
            foreach (var p in Process.GetProcessesByName("BongoCat"))
            {
                try { return p.MainModule.FileName; }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            return null;
        }

        public string Registry(string hive, string key, string name)
        {
            try
            {
                var root = hive == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
                using (var k = root.OpenSubKey(key)) return k == null ? null : k.GetValue(name) as string;
            }
            catch (System.Security.SecurityException) { return null; }
        }

        public bool FileExists(string path) { FileChecks++; return File.Exists(path); }
        public string ReadText(string path) { FileChecks++; return File.Exists(path) ? File.ReadAllText(path) : null; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder buf, uint len, uint flags);

        public string FinalPath(string dir)
        {
            const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, OPEN_EXISTING = 3, SHARE_ALL = 7;
            using (var h = CreateFileW(dir, 0, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (h.IsInvalid) return dir;
                var sb = new StringBuilder(1024);
                if (GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0) == 0) return dir;
                var s = sb.ToString();
                if (s.StartsWith(@"\\?\UNC\")) return @"\\" + s.Substring(8);
                return s.StartsWith(@"\\?\") ? s.Substring(4) : s;
            }
        }
    }

    public enum LocateKind { Found, Multiple, NotFound }

    public sealed class LocateResult
    {
        public LocateKind Kind;
        public string Path;
        public List<string> Candidates = new List<string>();
    }

    /// <summary>Finds the game folder (40-SPEC 5장). Uses the saved location without checking at start-up.</summary>
    public sealed class GameLocator
    {
        public const string AppId = "3419430";
        public const string DefaultDir = @"C:\Program Files (x86)\Steam\steamapps\common\BongoCat";
        private readonly ILocatorEnv _env;
        public GameLocator(ILocatorEnv env) { _env = env; }

        /// <summary>5.4: BongoCat.exe and BongoCat_Data\Managed\Assembly-CSharp.dll both exist.</summary>
        public bool IsGameFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            return _env.FileExists(Path.Combine(dir, "BongoCat.exe")) && _env.FileExists(Path.Combine(dir, "BongoCat_Data", "Managed", "Assembly-CSharp.dll"));
        }

        public static List<string> ParseLibraryFolders(string vdf)
        {
            var r = new List<string>();
            if (vdf == null) return r;
            foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
                r.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            return r;
        }

        public static string ParseInstallDir(string acf)
        {
            if (acf == null) return null;
            var m = Regex.Match(acf, "\"installdir\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
        }

        /// <summary>5.3 search order. Each candidate carries whether an app manifest backs it.</summary>
        public List<KeyValuePair<string, bool>> Candidates()
        {
            var r = new List<KeyValuePair<string, bool>>();
            Action<string, bool> add = (p, manifest) =>
            {
                if (string.IsNullOrEmpty(p)) return;
                var norm = p.TrimEnd('\\', '/').Replace('/', '\\');
                if (!r.Any(x => string.Equals(x.Key, norm, StringComparison.OrdinalIgnoreCase))) r.Add(new KeyValuePair<string, bool>(norm, manifest));
            };
            var running = _env.RunningGameExe();
            if (running != null) add(Path.GetDirectoryName(running), false);
            var steam = _env.Registry("HKCU", @"Software\Valve\Steam", "SteamPath") ?? _env.Registry("HKLM", @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            if (steam != null)
            {
                steam = steam.Replace('/', '\\');
                var libs = new List<string> { steam };
                libs.AddRange(ParseLibraryFolders(_env.ReadText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"))));
                foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var dir = ParseInstallDir(_env.ReadText(Path.Combine(lib, "steamapps", "appmanifest_" + AppId + ".acf")));
                    if (dir != null) add(Path.Combine(lib, "steamapps", "common", dir), true);
                }
            }
            add(DefaultDir, false);
            return r;
        }

        public LocateResult Locate()
        {
            var good = Candidates().Where(c => IsGameFolder(c.Key)).ToList();
            var distinct = new List<KeyValuePair<string, bool>>();
            foreach (var g in good)
            {
                var final = _env.FinalPath(g.Key);
                if (!distinct.Any(d => string.Equals(d.Key, final, StringComparison.OrdinalIgnoreCase))) distinct.Add(new KeyValuePair<string, bool>(final, g.Value));
            }
            if (distinct.Count == 0) return new LocateResult { Kind = LocateKind.NotFound };
            if (distinct.Count == 1) return new LocateResult { Kind = LocateKind.Found, Path = distinct[0].Key };
            var withManifest = distinct.Where(d => d.Value).ToList();
            if (withManifest.Count == 1) return new LocateResult { Kind = LocateKind.Found, Path = withManifest[0].Key };
            return new LocateResult { Kind = LocateKind.Multiple, Candidates = distinct.Select(d => d.Key).ToList() };
        }
    }

    public enum RecheckReason { FirstRun, RunningElsewhere, NoStatusWhileRunning, FileOperation, UserAsked }

    /// <summary>
    /// Keeps the saved game folder (40-SPEC 5.1–5.2): no check at start-up; re-locate only when something shows the
    /// saved location is wrong. A failed search never clears the saved location.
    /// </summary>
    public sealed class GameLocationService
    {
        private readonly GameLocator _locator;
        private readonly Func<string> _load;
        private readonly Action<string> _save;
        public event Action<string> Relocated;              // new path (notice "게임 위치가 바뀌어 다시 찾았어요")
        public event Action<string> Found;                  // first-run find (one-line notice)
        public event Action NotFound;                       // show folder picker + "open in Steam"
        public event Action<List<string>> ChooseAmong;      // several candidates
        public static readonly TimeSpan NoStatusLimit = TimeSpan.FromSeconds(30);

        public GameLocationService(GameLocator locator, Func<string> load, Action<string> save)
        {
            _locator = locator; _load = load; _save = save;
        }

        public string Saved { get { var s = _load(); return string.IsNullOrEmpty(s) ? null : s; } }

        /// <summary>Start-up: the saved path is used as-is (no file access). Only a missing value triggers a search.</summary>
        public string OnStart()
        {
            if (Saved != null) return Saved;
            return Search(RecheckReason.FirstRun);
        }

        public string Search(RecheckReason why)
        {
            var r = _locator.Locate();
            switch (r.Kind)
            {
                case LocateKind.Found:
                    bool changed = Saved != null && !string.Equals(Saved, r.Path, StringComparison.OrdinalIgnoreCase);
                    _save(r.Path);
                    if (why == RecheckReason.FirstRun) { var h = Found; if (h != null) h(r.Path); }
                    else if (changed) { var h = Relocated; if (h != null) h(r.Path); }
                    return r.Path;
                case LocateKind.Multiple:
                    { var h = ChooseAmong; if (h != null) h(r.Candidates); }
                    return Saved;
                default:
                    { var h = NotFound; if (h != null) h(); }
                    return Saved; // never cleared by a failed search
            }
        }

        /// <summary>Condition 1: the running game's folder differs from the saved one.</summary>
        public string OnGameProcess(string exePath)
        {
            if (exePath == null) return Saved;
            var dir = Path.GetDirectoryName(exePath);
            if (Saved != null && string.Equals(dir.TrimEnd('\\'), Saved.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return Saved;
            return Search(RecheckReason.RunningElsewhere);
        }

        /// <summary>Condition 2: game running but no status from the component for 30 s.</summary>
        public string OnNoStatus(bool gameRunning, TimeSpan silentFor)
        {
            if (!gameRunning || silentFor < NoStatusLimit) return Saved;
            if (_locator.IsGameFolder(Saved)) return Saved;
            return Search(RecheckReason.NoStatusWhileRunning);
        }

        /// <summary>Condition 3: before an operation on game files. Returns the folder to use, or null.</summary>
        public string ForFileOperation()
        {
            if (_locator.IsGameFolder(Saved)) return Saved;
            var p = Search(RecheckReason.FileOperation);
            return _locator.IsGameFolder(p) ? p : null;
        }

        /// <summary>User picked a folder; stored only if it is a game folder (5.4).</summary>
        public bool Choose(string dir)
        {
            if (!_locator.IsGameFolder(dir)) return false;
            _save(dir);
            return true;
        }
    }
}
