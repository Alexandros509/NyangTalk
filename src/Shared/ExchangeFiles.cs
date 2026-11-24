using System.IO;
using System.Threading;

namespace NyangTalk.Shared
{
    public static class ExchangeFiles
    {
        public const int MaxControlBytes = 64 * 1024;

        /// <summary>Read one atomic snapshot without preventing its writer from replacing the path.</summary>
        public static byte[] ReadAllBytes(string path, int maxBytes = MaxControlBytes)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return ReadSnapshot(path, maxBytes); }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xffff;
                    // A replacement may briefly make the old name unavailable or hold a sharing/lock handle.
                    if (attempt >= 3 || !(ex is FileNotFoundException || code == 32 || code == 33)) throw;
                }
                Thread.Sleep(1);
            }
        }

        private static byte[] ReadSnapshot(string path, int maxBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > maxBytes) throw new InvalidDataException("Exchange control file is too large");
                var bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) throw new EndOfStreamException("Exchange snapshot changed while reading");
                    read += count;
                }
                return bytes;
            }
        }
    }
}
