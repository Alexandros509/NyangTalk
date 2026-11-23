using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NyangTalk.Tests
{
    public static class DistributionTests
    {
        private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp" };

        /// <summary>Unix corlibs pass the managed contract checks but fail during Windows Unity startup (System.Native).</summary>
        [Test("40-1.1,10-11.4")]
        public static void FullCorlibUsesTheWindowsPlatformProfile()
        {
            var path = Path.Combine(Ctx.Root, "third_party", "BepInEx", "payload", "BepInEx", "unstripped", "mscorlib.dll");
            var corlib = NyangTalk.Adapters.Metadata.MetadataReader.Read(path);
            A.True(corlib.Types.ContainsKey("Interop+Kernel32"), "full corlib includes Windows native file/time support");
            A.False(corlib.Types.ContainsKey("Interop+Sys"), "Unix System.Native profile must never ship in the Windows payload");
        }

        /// <summary>70-3.7 and 01-3.2: no cat pictures or game code in what we ship; 30-7.2.3: every bundled asset has a license entry.</summary>
        [Test("70-3.7,30-7.2.3,01-3.2")]
        public static void ShippedFilesHaveNoGameContent()
        {
            var listed = File.ReadAllText(Path.Combine(Ctx.Root, "docs", "design-assets-licenses.md"));
            foreach (var dir in new[] { "src", "assets" })
            {
                var d = Path.Combine(Ctx.Root, dir);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ImageExt.Contains(ext)) A.True(listed.Contains(Path.GetFileName(f)), "image not in design-assets-licenses.md: " + f);
                    A.False(ext == ".dll" || ext == ".exe", "binary in source tree: " + f);
                }
            }
            var exe = Path.Combine(Ctx.Root, "build", "NyangTalk.exe");
            if (!File.Exists(exe)) { Ctx.Measurements.Add("배포 파일 검사: build\\NyangTalk.exe 없음(-Release 빌드 전) — 소스 트리만 검사"); return; }
            var names = Assembly.ReflectionOnlyLoadFrom(exe).GetManifestResourceNames();
            // the only binaries allowed besides our own are the official BepInEx files listed with their hashes
            var official = File.ReadAllLines(Path.Combine(Ctx.Root, "third_party", "BepInEx", "hashes.txt")).Where(l => l.Length > 66)
                .ToDictionary(l => "payload/" + l.Substring(66), l => l.Substring(0, 64));
            var asm = Assembly.ReflectionOnlyLoadFrom(exe);
            foreach (var n in names)
            {
                // the only picture allowed is the app icon listed in design-assets-licenses.md
                A.False(ImageExt.Contains(Path.GetExtension(n).ToLowerInvariant()) && !(n == "app.ico" && listed.Contains("app.ico")), "picture embedded: " + n);
                bool ours = n == "app.ico" || n == "contract.json" || n == "LICENSE" || n == "THIRD-PARTY-NOTICES.md" || n.StartsWith("payload/BepInEx/plugins/NyangTalk/") || n.StartsWith("licenses/") || n.StartsWith("fonts/");
                if (ours) continue;
                string expected;
                A.True(official.TryGetValue(n, out expected), "unexpected embedded file: " + n);
                using (var s = asm.GetManifestResourceStream(n))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    A.Eq(expected, BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(), "official BepInEx file unchanged: " + n);
            }
            A.Eq(official.Count, names.Count(n => official.ContainsKey(n)), "every official BepInEx file is embedded");
            var fontFiles = Directory.GetFiles(Path.Combine(Ctx.Root, "third_party", "fonts")).Select(Path.GetFileName).ToList();
            var licensed = File.ReadAllText(Path.Combine(Ctx.Root, "third_party", "SOURCES.md"));
            foreach (var f in fontFiles) A.True(names.Contains("fonts/" + f) && licensed.Contains(f.Split('-')[0]), "font embedded and listed in SOURCES.md: " + f);
            var gameAsm = new[] { "Assembly-CSharp", "Heathen.Steamworks", "com.rlabrecque", "UnityEngine" };
            foreach (var n in names) A.False(gameAsm.Any(g => n.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0), "game binary embedded: " + n);
        }

        [Test("90-1")]
        public static void BundledFontsLoadAndMeasure()
        {
            foreach (var fam in NyangTalk.App.Platform.Fonts.Bundled.Keys)
            {
                var f = NyangTalk.App.Platform.Fonts.Family(fam);
                A.True(f != null && f.Name == fam, "bundled family loads: " + fam);
                A.True(f.IsStyleAvailable(System.Drawing.FontStyle.Bold), fam + " has bold");
                using (var font = NyangTalk.App.Platform.Fonts.Create(fam, 10f))
                {
                    A.Eq(fam, font.FontFamily.Name, "font object uses the bundled family");
                    var w = System.Windows.Forms.TextRenderer.MeasureText("가나다 ABC", font).Width;
                    A.True(w > 20, fam + " measures Korean text with GDI (" + w + ")");
                }
            }
            A.Eq("pretendard", NyangTalk.Domain.Preferences.Settings.Def("screen.font").Default, "Pretendard is the default");
        }

        [Test("40-1.1,LGPL")]
        public static void UnstrippedLibrariesAndLgplSourcesShipped()
        {
            // source tree: every payload file is listed with its hash, the loader points at the full libraries
            var payload = Path.Combine(Ctx.Root, "third_party", "BepInEx", "payload");
            var listed = File.ReadAllLines(Path.Combine(Ctx.Root, "third_party", "BepInEx", "hashes.txt")).Where(l => l.Length > 66).Select(l => l.Substring(66)).ToList();
            foreach (var f in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
                A.True(listed.Contains(f.Substring(payload.Length + 1).Replace('\\', '/')), "hash listed for " + f.Substring(payload.Length + 1));
            A.Eq(16, Directory.GetFiles(Path.Combine(payload, "BepInEx", "unstripped"), "*.dll").Length, "15 corlibs + Newtonsoft.Json");
            A.True(File.ReadAllLines(Path.Combine(payload, "doorstop_config.ini")).Any(l => l.Trim() == @"dll_search_path_override = BepInEx\unstripped"), "loader searches the full libraries first");
            string problem;
            A.Eq(2, NyangTalk.Adapters.Update.ReleaseBuilder.SourceArchives(Path.Combine(Ctx.Root, "third_party", "sources"), out problem).Count, "LGPL sources present and unchanged: " + problem);
            var notices = File.ReadAllText(Path.Combine(Ctx.Root, "THIRD-PARTY-NOTICES.md"));
            A.True(notices.Contains("LGPL-2.1") && notices.Contains("BepInEx-5.4.23.6-src.zip") && notices.Contains("UnityDoorstop-4.6.0-src.zip"), "notices say where the LGPL sources are");

            // release exe: what the about screen's license list shows comes from these embedded files
            var exe = Path.Combine(Ctx.Root, "build", "NyangTalk.exe");
            if (!File.Exists(exe)) { Ctx.Measurements.Add(@"LGPL·전체판 배포 검사: build\NyangTalk.exe 없음 — 소스 트리만 검사"); return; }
            var asm = Assembly.ReflectionOnlyLoadFrom(exe);
            var names = asm.GetManifestResourceNames();
            foreach (var n in new[] { "licenses/Mono-class-libraries-LICENSE.txt", "licenses/Newtonsoft.Json-MIT.txt", "licenses/BepInEx-LGPL-2.1.txt", "licenses/UnityDoorstop-LGPL-2.1.txt", "THIRD-PARTY-NOTICES.md" })
                A.True(names.Contains(n), "license list shows " + n);
            using (var st = asm.GetManifestResourceStream("THIRD-PARTY-NOTICES.md"))
            using (var r = new StreamReader(st))
            {
                var text = r.ReadToEnd();
                A.True(text.Contains("attached to every NyangTalk release"), "the embedded notices carry the LGPL source statement");
            }
            if (names.Any(n => n.StartsWith("payload/BepInEx/plugins/NyangTalk/")))
            {
                A.True(names.Contains("payload/BepInEx/unstripped/mscorlib.dll") && names.Contains("payload/BepInEx/unstripped/Newtonsoft.Json.dll"), "connector bundle carries the full libraries");
                A.True(names.Contains("payload/BepInEx/plugins/NyangTalk/licenses/Mono-class-libraries-LICENSE.txt"), "their license goes into the game folder too");
            }
        }
    }
}
