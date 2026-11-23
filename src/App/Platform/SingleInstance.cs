using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NyangTalk.App.Platform
{
    /// <summary>
    /// 95-SPEC 1.1: one app per data folder. A second start signals the first (which comes to the front) and ends.
    /// After "지금 재시작" the new process waits a little for the old one to let go instead of signalling it.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activate;
        private readonly RegisteredWaitHandle _wait;
        private Action _onActivate;

        private SingleInstance(Mutex m, EventWaitHandle e) { _mutex = m; _activate = e; _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (s, t) => { var h = _onActivate; if (h != null) h(); }, null, Timeout.Infinite, false); }

        /// <summary>Name shared by every copy that uses the same data folder (demo and real data stay apart).</summary>
        public static string NameFor(string dataRoot, string role)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes((dataRoot ?? "").TrimEnd('\\').ToLowerInvariant()));
                return @"Local\NyangTalk-" + role + "-" + BitConverter.ToString(h, 0, 8).Replace("-", "");
            }
        }

        /// <summary>Takes the single-instance lock, waiting up to <paramref name="wait"/>; null when another copy holds it.</summary>
        public static SingleInstance TryAcquire(string name, TimeSpan wait)
        {
            bool created;
            var m = new Mutex(false, name, out created);
            bool owned;
            try { owned = m.WaitOne(wait); }
            catch (AbandonedMutexException) { owned = true; }   // the previous holder ended without letting go
            if (!owned) { m.Dispose(); return null; }
            return new SingleInstance(m, new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate"));
        }

        /// <summary>Asks the running copy to come to the front; false when there is none listening.</summary>
        public static bool SignalOther(string name)
        {
            EventWaitHandle e;
            if (!EventWaitHandle.TryOpenExisting(name + "-activate", out e)) return false;
            using (e) return e.Set();
        }

        /// <summary>True when some copy holds the lock (used by the game watcher).</summary>
        public static bool IsHeld(string name)
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(name, out m)) return false;
            using (m)
            {
                bool got;
                try { got = m.WaitOne(0); }
                catch (AbandonedMutexException) { got = true; }
                if (got) { m.ReleaseMutex(); return false; }
                return true;
            }
        }

        /// <summary>Called on a worker thread whenever another start asks this copy to come to the front.</summary>
        public void OnActivate(Action a) { _onActivate = a; }

        public void Dispose()
        {
            _wait.Unregister(null);
            _activate.Dispose();
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
