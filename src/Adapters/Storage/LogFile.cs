using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NyangTalk.Adapters.Storage
{
    /// <summary>
    /// App log in the app data (95-SPEC 2.5). Over 5 MB the file becomes nyangtalk.log.1 (the old .1 becomes .2, the old
    /// .2 is deleted), so at most two earlier files are kept. Callers pass lines that are already cleaned of chat text,
    /// names, full Steam IDs and the user folder; this class only stores them. A write that fails is dropped, never thrown.
    /// </summary>
    public sealed class LogFile
    {
        public const long DefaultLimit = 5L * 1024 * 1024;
        public const int KeepOld = 2;
        public const string FileName = "nyangtalk.log";

        public long Limit = DefaultLimit;
        private readonly string _path;
        private readonly object _lock = new object();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public LogFile(string dir) { _path = System.IO.Path.Combine(dir, FileName); }

        public string Path { get { return _path; } }
        public string OldPath(int n) { return _path + "." + n; }

        public bool Write(string line)
        {
            var bytes = Utf8.GetBytes((line ?? "").Replace("\r", " ").Replace("\n", " ") + "\r\n");
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                    var fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 0 && fi.Length + bytes.Length > Limit) Rotate();
                    using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        fs.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        private void Rotate()
        {
            var oldest = OldPath(KeepOld);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = KeepOld - 1; i >= 1; i--)
                if (File.Exists(OldPath(i))) File.Move(OldPath(i), OldPath(i + 1));
            File.Move(_path, OldPath(1));
        }

        /// <summary>The last <paramref name="count"/> lines (the previous file too when the current one is short); empty when unreadable.</summary>
        public List<string> Tail(int count)
        {
            var lines = new List<string>();
            lock (_lock)
            {
                foreach (var p in new[] { _path, OldPath(1) })
                {
                    if (lines.Count >= count) break;
                    try
                    {
                        if (!File.Exists(p)) continue;
                        string text;
                        using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var r = new StreamReader(fs, Utf8)) text = r.ReadToEnd();
                        var got = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        lines.InsertRange(0, got.Skip(Math.Max(0, got.Length - (count - lines.Count))));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return lines;
        }
    }
}
