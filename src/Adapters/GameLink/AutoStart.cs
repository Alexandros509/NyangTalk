using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>The user's start-up programs list (HKCU Run); a fake in checks, never the real registry there.</summary>
    public interface IRunKey
    {
        string Get(string name);
        bool Set(string name, string value, out string why);
        bool Delete(string name, out string why);
    }

    public sealed class RealRunKey : IRunKey
    {
        private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public string Get(string name)
        {
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path)) return k == null ? null : k.GetValue(name) as string; }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public bool Set(string name, string value, out string why)
        {
            why = null;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Path)) k.SetValue(name, value); return true; }
            catch (System.Security.SecurityException ex) { why = ex.Message; }
            catch (UnauthorizedAccessException ex) { why = ex.Message; }
            catch (IOException ex) { why = ex.Message; }
            return false;
        }

        public bool Delete(string name, out string why)
        {
            why = null;
            try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path, true)) { if (k != null && k.GetValue(name) != null) k.DeleteValue(name); } return true; }
            catch (System.Security.SecurityException ex) { why = ex.Message; }
            catch (UnauthorizedAccessException ex) { why = ex.Message; }
            catch (IOException ex) { why = ex.Message; }
            return false;
        }
    }

    /// <summary>
    /// 95-SPEC 1.2: the path written into Windows (start-up list, shortcuts) is a fixed copy of the app in the app data.
    /// Any NyangTalk.exe hands over to the newest installed version on start, so this copy keeps opening the latest one
    /// after updates.
    /// </summary>
    public static class FixedLaunchPath
    {
        public static string For(string dataRoot) { return System.IO.Path.Combine(dataRoot, "NyangTalk.exe"); }

        /// <summary>Puts the running exe at the fixed path if nothing is there yet; returns the path or null with a reason.</summary>
        public static string Ensure(string dataRoot, string selfExe, out string why)
        {
            why = null;
            var target = For(dataRoot);
            try
            {
                if (File.Exists(target)) return target;
                if (string.IsNullOrEmpty(selfExe) || !File.Exists(selfExe)) { why = "app file not found"; return null; }
                Directory.CreateDirectory(dataRoot);
                File.Copy(selfExe, target, false);
                return target;
            }
            catch (IOException ex) { why = ex.Message; }
            catch (UnauthorizedAccessException ex) { why = ex.Message; }
            return null;
        }
    }

    /// <summary>
    /// 95-SPEC 2.1 "봉고캣을 켜면 함께 열기": one start-up entry for the app's name, pointing at the fixed path with --watch-game.
    /// Turning it off removes the entry; nothing else is left outside the app data.
    /// </summary>
    public sealed class AutoStart
    {
        public const string ValueName = "NyangTalk";
        public const string WatchArg = "--watch-game";
        private readonly IRunKey _run;

        public AutoStart(IRunKey run) { _run = run; }

        public static string CommandFor(string fixedExe) { return "\"" + fixedExe + "\" " + WatchArg; }

        public bool IsOn { get { return _run.Get(ValueName) != null; } }

        public bool TurnOn(string fixedExe, out string why) { return _run.Set(ValueName, CommandFor(fixedExe), out why); }

        public bool TurnOff(out string why) { return _run.Delete(ValueName, out why); }
    }

    /// <summary>
    /// The background watcher (no window, one per data folder): opens the app when the registered game starts. It opens it
    /// only on the change from "not running" to "running", so an app the user closed during that game stays closed. It ends,
    /// removing its own start-up entry, when the fixed path is gone; it ends quietly when the entry was removed.
    /// </summary>
    public sealed class GameWatcher
    {
        public Func<IEnumerable<string>> RunningGameExes = () => Enumerable.Empty<string>();
        public Func<string> GameDir = () => null;
        public Func<string, bool> FileExists = File.Exists;
        public Action OpenApp = () => { };
        private readonly AutoStart _auto;
        private readonly IRunKey _run;
        private readonly string _fixedExe;
        private bool _wasRunning, _first = true;
        public bool Ended { get; private set; }
        public string EndReason { get; private set; }

        public GameWatcher(IRunKey run, string fixedExe) { _run = run; _auto = new AutoStart(run); _fixedExe = fixedExe; }

        public void Tick()
        {
            if (Ended) return;
            if (!_auto.IsOn) { Ended = true; EndReason = "turned off"; return; }
            if (!FileExists(_fixedExe))
            {
                string why;
                _auto.TurnOff(out why);   // the app folder was removed: leave nothing behind
                Ended = true; EndReason = "app removed";
                return;
            }
            var dir = GameDir();
            var exe = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "BongoCat.exe");
            bool running = exe != null && RunningGameExes().Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase));
            // a game already running when the watcher starts (Windows sign-in) does not open the app by itself
            if (running && !_wasRunning && !_first) OpenApp();
            _wasRunning = running;
            _first = false;
        }
    }
}
