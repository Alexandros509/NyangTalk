using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace NyangTalk.Adapters.GameLink
{
    /// <summary>The game process as the reinstall flow sees it; replaced by a fake in checks (the real game is never touched there).</summary>
    public interface IGameControl
    {
        bool IsRunning { get; }
        /// <summary>Asks the game to close (its window's close message, never a forced kill) and waits.</summary>
        bool RequestClose(TimeSpan wait, out string why);
        bool LaunchViaSteam(out string why);
        /// <summary>Runs this app's installer step with administrator rights for the given game folder; true when it installed.</summary>
        bool InstallElevated(string gameDir, out string why);
    }

    public enum ReinstallOutcome { Done, NothingToDo, Cancelled, GameDidNotClose, Failed, Relaunched, RelaunchFailed }

    public sealed class ReinstallReport
    {
        public ReinstallOutcome Outcome;
        public InstallResult Install;
        public bool Elevated;
        public bool GameClosed;      // the flow closed the game (it stays closed when the install fails)
        public string Why;
    }

    /// <summary>
    /// "연결 다시 설치" (40-SPEC 6장): if the game is running, ask once; then close the game, install, and start the game
    /// again through Steam. Stops with a reason when the game does not close. Only the install step is retried with
    /// administrator rights when the game folder needs them.
    /// </summary>
    public sealed class ReinstallFlow
    {
        public IGameControl Game;
        public Func<string, InstallResult> Install;          // game folder → result
        public Func<bool> ConfirmRestart = () => false;      // the one question when the game is running
        public TimeSpan CloseWait = TimeSpan.FromSeconds(20);

        public ReinstallReport Run(string gameDir)
        {
            var rep = new ReinstallReport();
            bool wasRunning = Game.IsRunning;
            if (wasRunning)
            {
                if (!ConfirmRestart()) { rep.Outcome = ReinstallOutcome.Cancelled; return rep; }
                string why;
                if (!Game.RequestClose(CloseWait, out why)) { rep.Outcome = ReinstallOutcome.GameDidNotClose; rep.Why = why; return rep; }
                rep.GameClosed = true;
            }
            rep.Install = Install(gameDir);
            if (rep.Install.Outcome == InstallOutcome.NeedsElevation)
            {
                string why;
                rep.Elevated = true;
                if (!Game.InstallElevated(gameDir, out why)) { rep.Outcome = ReinstallOutcome.Failed; rep.Why = why; return rep; }
                // do not trust the helper's exit code alone: the same check again must now find the files in place
                var check = Install(gameDir);
                if (check.Outcome != InstallOutcome.NothingToDo && check.Outcome != InstallOutcome.Done) { rep.Install = check; rep.Outcome = ReinstallOutcome.Failed; rep.Why = "not-verified:" + check.Outcome; return rep; }
                rep.Install = new InstallResult { Outcome = InstallOutcome.Done };
            }
            if (rep.Install.Outcome != InstallOutcome.Done && rep.Install.Outcome != InstallOutcome.NothingToDo) { rep.Outcome = ReinstallOutcome.Failed; return rep; }
            rep.Outcome = rep.Install.Outcome == InstallOutcome.Done ? ReinstallOutcome.Done : ReinstallOutcome.NothingToDo;
            if (wasRunning)
            {
                string why;
                rep.Outcome = Game.LaunchViaSteam(out why) ? ReinstallOutcome.Relaunched : ReinstallOutcome.RelaunchFailed;
                rep.Why = why;
            }
            return rep;
        }
    }

    /// <summary>The real game: found by process name, closed politely, started through Steam.</summary>
    public sealed class RealGameControl : IGameControl
    {
        public const string ProcessName = "BongoCat";
        public string ExePath;      // this app, for the elevated install step
        public string DataFolder;

        public bool IsRunning { get { return Process.GetProcessesByName(ProcessName).Length > 0; } }

        public bool RequestClose(TimeSpan wait, out string why)
        {
            why = null;
            var procs = Process.GetProcessesByName(ProcessName);
            try
            {
                bool noWindow = false;
                foreach (var p in procs)
                {
                    try { if (!p.CloseMainWindow()) noWindow = true; }
                    catch (InvalidOperationException) { }   // already gone
                }
                var until = DateTime.UtcNow + wait;
                foreach (var p in procs)
                {
                    try
                    {
                        var left = until - DateTime.UtcNow;
                        if (!p.WaitForExit((int)Math.Max(0, left.TotalMilliseconds))) { why = noWindow ? "no-window" : "timeout:" + (int)wait.TotalSeconds; return false; }
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception ex) { why = ex.Message; return false; }
                }
                if (IsRunning) { why = "still-running"; return false; }   // e.g. a new instance started meanwhile
                return true;
            }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        public bool LaunchViaSteam(out string why)
        {
            why = null;
            try { Process.Start(new ProcessStartInfo("steam://rungameid/" + GameLocator.AppId) { UseShellExecute = true }); return true; }
            catch (Win32Exception ex) { why = ex.Message; return false; }
        }

        public bool InstallElevated(string gameDir, out string why)
        {
            why = null;
            try
            {
                var args = "--install-connector \"" + gameDir + "\"" + (DataFolder == null ? "" : " --data \"" + DataFolder + "\"");
                using (var p = Process.Start(new ProcessStartInfo(ExePath, args) { UseShellExecute = true, Verb = "runas" }))
                {
                    if (!p.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds)) { why = "helper-timeout"; return false; }
                    if (p.ExitCode == 0) return true;
                    why = "exit:" + p.ExitCode;   // 3: install refused (see install-helper.log), 4: error
                    return false;
                }
            }
            catch (Win32Exception ex) { why = ex.NativeErrorCode == 1223 ? "declined" : ex.Message; return false; }   // 1223: the user said no
        }
    }
}
