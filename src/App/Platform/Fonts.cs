using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NyangTalk.App.Platform
{
    /// <summary>
    /// Bundled fonts (Pretendard, Noto Sans KR, SUIT; SIL OFL 1.1, see third_party/SOURCES.md).
    /// A font is loaded only when chosen. It is registered twice: for GDI+ (PrivateFontCollection) and for GDI
    /// text drawing (AddFontMemResourceEx, process-private), so both Font objects and TextRenderer find it.
    /// </summary>
    public static class Fonts
    {
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        public static readonly Dictionary<string, string[]> Bundled = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Pretendard", new[] { "Pretendard-Regular.ttf", "Pretendard-Bold.ttf" } },
            { "Noto Sans KR", new[] { "NotoSansKR-Regular.otf", "NotoSansKR-Bold.otf" } },
            { "SUIT", new[] { "SUIT-Regular.ttf", "SUIT-Bold.ttf" } },
        };

        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> Loaded = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<IntPtr> Pinned = new List<IntPtr>();   // memory must stay alive while the fonts are used

        private static byte[] ReadFontFile(string file)
        {
            var asm = typeof(Fonts).Assembly;
            using (var s = asm.GetManifestResourceStream("fonts/" + file))
            {
                if (s != null) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
            // development builds: build\fonts next to the exe
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", file);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>Loads a bundled family on first use. Returns null if it is not bundled or could not be loaded.</summary>
        public static FontFamily Family(string name)
        {
            FontFamily fam;
            if (Loaded.TryGetValue(name, out fam)) return fam;
            string[] files;
            if (!Bundled.TryGetValue(name, out files)) return null;
            foreach (var f in files)
            {
                var data = ReadFontFile(f);
                if (data == null) { Loaded[name] = null; return null; }
                var ptr = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, ptr, data.Length);
                Pinned.Add(ptr);
                uint count = 0;
                AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, ref count);
                Collection.AddMemoryFont(ptr, data.Length);
            }
            fam = Collection.Families.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            Loaded[name] = fam;
            return fam;
        }

        /// <summary>Creates a font by family name, preferring the bundled copy; falls back to the system UI font.</summary>
        public static Font Create(string name, float size, FontStyle style = FontStyle.Regular)
        {
            var fam = Family(name);
            try
            {
                if (fam != null)
                {
                    if (!fam.IsStyleAvailable(style)) style = FontStyle.Regular;
                    return new Font(fam, size, style, GraphicsUnit.Point);
                }
                return new Font(name, size, style, GraphicsUnit.Point);
            }
            catch (ArgumentException) { return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style); }
        }
    }
}
