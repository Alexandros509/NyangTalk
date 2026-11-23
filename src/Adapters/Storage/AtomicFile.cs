using System;
using System.IO;
using System.Text;
using System.Threading;

namespace NyangTalk.Adapters.Storage
{
    /// <summary>Write to a temporary file, then swap it in; short retries when another process holds the file (90-SPEC 2장).</summary>
    public static class AtomicFile
    {
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        public static int Retries = 8;
        public static int RetryDelayMs = 40;   // grows: 40, 80, 120, … (about 1.4 s in all before giving up)
        /// <summary>Test hook: when set, every write throws this (simulates disk or lock failure).</summary>
        public static Func<string, Exception> FailureInjector;

        public static void WriteAllText(string path, string text)
        {
            WriteAllBytes(path, Utf8.GetBytes(text));
        }

        public static void WriteAllBytes(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Exception last = null;
            for (int attempt = 0; attempt <= Retries; attempt++)
            {
                var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                try
                {
                    var inj = FailureInjector;
                    if (inj != null) { var e = inj(path); if (e != null) throw e; }
                    File.WriteAllBytes(tmp, data);
                    if (File.Exists(path)) File.Replace(tmp, path, null, true);
                    else File.Move(tmp, path);
                    return;
                }
                catch (IOException ex) { last = ex; }
                catch (UnauthorizedAccessException ex) { last = ex; }
                finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                if (attempt < Retries) Thread.Sleep(RetryDelayMs * (attempt + 1));
            }
            throw new IOException("could not write " + path, last);
        }

        /// <summary>Reads with short retries; null if the file does not exist.</summary>
        public static byte[] ReadAllBytes(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        var buf = new byte[fs.Length];
                        int read = 0;
                        while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                        if (read != buf.Length) Array.Resize(ref buf, read);
                        return buf;
                    }
                }
                catch (FileNotFoundException) { return null; }
                catch (IOException) { if (attempt >= Retries) throw; }
                Thread.Sleep(RetryDelayMs);
            }
        }

        public static string ReadAllText(string path)
        {
            var b = ReadAllBytes(path);
            return b == null ? null : Utf8.GetString(b);
        }

        public static void AppendLines(string path, System.Collections.Generic.IEnumerable<string> lines)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l).Append('\n');
            var data = Utf8.GetBytes(sb.ToString());
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var inj = FailureInjector;
                    if (inj != null) { var e = inj(path); if (e != null) throw e; }
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(data, 0, data.Length);
                        fs.Flush(true);
                    }
                    return;
                }
                catch (IOException) { if (attempt >= Retries) throw; }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }
}
