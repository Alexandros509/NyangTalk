using System;
using System.Collections.Generic;

namespace NyangTalk.Shared
{
    /// <summary>Successful work clears only that operation's current fault; unrelated faults stay visible.</summary>
    public sealed class ComponentErrors
    {
        private readonly SortedDictionary<string, string> _faults = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _reports = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Error
        {
            get
            {
                if (_faults.Count == 0) return null;
                var messages = new List<string>();
                foreach (var fault in _faults) messages.Add(fault.Key + ": " + fault.Value);
                return string.Join("; ", messages.ToArray());
            }
        }
        public bool Set(string operation, string reason)
        {
            string previous;
            if (_faults.TryGetValue(operation, out previous) && previous == reason) return false;
            _faults[operation] = reason;
            return true;
        }
        public void Clear(string operation) { _faults.Remove(operation); _reports.Remove(operation); }
        public bool Run(string operation, Action work, Action<Exception> failed)
        {
            try { work(); Clear(operation); return true; }
            catch (Exception ex)
            {
                string signature = ex.GetType().FullName + ":" + ex.HResult + ":" + ex.Message;
                string previous;
                bool report = !_reports.TryGetValue(operation, out previous) || previous != signature;
                Set(operation, ex.GetType().Name); _reports[operation] = signature;
                if (report && failed != null) failed(ex);
                return false;
            }
        }
    }
}
