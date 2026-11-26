using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NyangTalk.Adapters.Update;

namespace NyangTalk.Tools
{
    /// <summary>
    /// Publisher tool (20-SPEC 6장). Never stores the password; the key file is password-encrypted and portable.
    ///   keygen &lt;out.key&gt;                       new key pair; prints the public key to embed in the app
    ///   release &lt;version&gt; &lt;exe&gt; &lt;key&gt; &lt;sequence&gt; &lt;assetUrlBase&gt; --notes &lt;file.json&gt; [--channel beta] [--game-payload-changed] [--breaking] [--prev X]
    /// The notes file holds the ko/en/ja update note shown in the app ({"ko": ..., "en": ..., "ja": ...}); all three are required.
    /// Upload happens only after typing "yes" at the summary, using `gh release create`. Nothing is uploaded otherwise.
    /// </summary>
    public static class ReleaseToolProgram
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "keygen") return KeyGen(args[1]);
                if (args.Length >= 6 && args[0] == "release")
                {
                    int relaunched;
                    if (RunFromCopyIfInBuild(args, out relaunched)) return relaunched;
                    return Release(args);
                }
                Console.Error.WriteLine("usage: keygen <out.key> | release <version> <exe> <key> <sequence> <assetUrlBase> --notes <file.json> [--channel beta] [--game-payload-changed] [--breaking] [--prev X]");
                return 1;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
        }

        private const string CopyMark = "NYANGTALK_RELEASE_TOOL_COPY";

        /// <summary>
        /// The release runs build.ps1, which rebuilds build\NyangTalk.*.dll — the files this tool itself has loaded when it
        /// runs from build\. So it copies itself and those libraries to a temporary folder and runs the copy (same working
        /// folder, same console); the copy is removed afterwards. Uses only System types: nothing from build\ loads first.
        /// </summary>
        private static bool RunFromCopyIfInBuild(string[] args, out int exitCode)
        {
            exitCode = 0;
            var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var build = Path.Combine(Directory.GetCurrentDirectory(), "build");
            if (Environment.GetEnvironmentVariable(CopyMark) != null || !string.Equals(Path.GetFullPath(here), Path.GetFullPath(build), StringComparison.OrdinalIgnoreCase)) return false;
            var tmp = Path.Combine(Path.GetTempPath(), "nyangtalk-release-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                var exe = Path.Combine(tmp, Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location));
                File.Copy(System.Reflection.Assembly.GetEntryAssembly().Location, exe);
                foreach (var dll in Directory.GetFiles(here, "NyangTalk.*.dll")) File.Copy(dll, Path.Combine(tmp, Path.GetFileName(dll)));
                var psi = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() };
                psi.EnvironmentVariables[CopyMark] = "1";
                using (var p = Process.Start(psi)) { p.WaitForExit(); exitCode = p.ExitCode; }
                return true;
            }
            finally { try { Directory.Delete(tmp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        private static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            // Windows argument rules: backslashes before a quote (or the closing quote) are doubled
            var sb = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (var ch in a)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(ch); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var pw = new System.Text.StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (pw.Length > 0) pw.Length--; continue; }
                pw.Append(k.KeyChar);
            }
            Console.WriteLine();
            return pw.ToString();
        }

        private static int KeyGen(string path)
        {
            if (File.Exists(path)) { Console.Error.WriteLine("refusing to overwrite " + path); return 1; }
            var pw = ReadPassword("password: ");
            if (pw.Length < 12 || pw != ReadPassword("again: ")) { Console.Error.WriteLine("password too short or mismatch"); return 1; }
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                File.WriteAllText(path, KeyFile.Create(key, pw));
                var pub = Signatures.PublicOf(key);
                Console.WriteLine("public key (embed in AppHost.UpdateKeys): " + Convert.ToBase64String(pub));
                Console.WriteLine("fingerprint: " + Signatures.Fingerprint(pub));
                Console.WriteLine("Keep an offline copy. Never commit the key file.");
            }
            return 0;
        }

        private static int Release(string[] a)
        {
            var root = Directory.GetCurrentDirectory();
            var req = new ReleaseRequest
            {
                Version = a[1], ExePath = a[2], Sequence = long.Parse(a[4]), AssetUrlBase = a[5],
                Channel = a.Contains("--beta") || (Array.IndexOf(a, "--channel") >= 0 && a[Array.IndexOf(a, "--channel") + 1] == "beta") ? "beta" : "stable",
                GamePayloadChanged = a.Contains("--game-payload-changed"), PublicApiBreaking = a.Contains("--breaking"),
                PreviousPublicVersion = Array.IndexOf(a, "--prev") >= 0 ? a[Array.IndexOf(a, "--prev") + 1] : null,
                Changelog = File.Exists(Path.Combine(root, "CHANGELOG.md")) ? File.ReadAllText(Path.Combine(root, "CHANGELOG.md")) : null,
                SourceDir = Path.Combine(root, "third_party", "sources")
            };
            int notesAt = Array.IndexOf(a, "--notes");
            if (notesAt >= 0 && notesAt + 1 < a.Length) req.Notes = ReleaseBuilder.ParseNotes(File.ReadAllText(a[notesAt + 1]));
            // stop before the checks and the password when the notes are not complete (they are signed and cannot be added later)
            var notesProblem = ReleaseBuilder.CheckNotes(req.Notes);
            if (notesProblem != null) { Console.Error.WriteLine("stopped: " + notesProblem + " (use --notes <file.json>)"); return 2; }
            req.ExistingTags.AddRange(Run("git", "tag --list").Split('\n').Select(t => t.Trim()).Where(t => t.Length > 0));
            req.ChecksPassed = RunCode("powershell", "-NoProfile -ExecutionPolicy Bypass -File build.ps1") == 0;
            if (!req.ChecksPassed) { Console.Error.WriteLine("stopped: checks failed"); return 2; }
            // Validate against the keys shipped in the chosen executable, including a wrong-key selection.
            var assembly = System.Reflection.Assembly.LoadFile(Path.GetFullPath(req.ExePath));
            var embedded = (IList<byte[]>)assembly.GetType("NyangTalk.App.AppHost", true).GetField("UpdateKeys").GetValue(null);
            var pw = ReadPassword("key password: ");
            using (var key = KeyFile.Open(File.ReadAllText(a[3]), pw))
            {
                var res = ReleaseBuilder.Run(req, key, embedded, summary =>
                {
                    Console.WriteLine(summary);
                    Console.Write("upload? type yes: ");
                    return Console.ReadLine() == "yes";
                }, (v, json, sig) =>
                {
                    var dir = Path.Combine(root, "build", "release-" + v);
                    Directory.CreateDirectory(dir);
                    var name = (req.Channel == "beta" ? "update-beta.json" : "update.json");
                    File.WriteAllText(Path.Combine(dir, name), json);
                    File.WriteAllText(Path.Combine(dir, name + ".sig"), sig);
                    var exeName = "NyangTalk-" + v + ".exe";
                    File.Copy(req.ExePath, Path.Combine(dir, exeName), true);
                    var pre = req.Channel == "beta" ? " --prerelease" : "";
                    // LGPL-2.1 sources of the shipped BepInEx and UnityDoorstop go with every release (third_party/SOURCES.md)
                    string problem;
                    var sources = ReleaseBuilder.SourceArchives(req.SourceDir, out problem);
                    if (problem != null) throw new IOException(problem);
                    var attach = string.Concat(sources.Select(p => " \"" + p + "\""));
                    if (RunCode("gh", "release create v" + v + pre + " \"" + Path.Combine(dir, exeName) + "\" \"" + Path.Combine(dir, name) + "\" \"" + Path.Combine(dir, name + ".sig") + "\"" + attach) != 0)
                        throw new IOException("gh release create failed");
                });
                Console.WriteLine(res.Uploaded ? "uploaded" : "stopped: " + res.StoppedAt);
                return res.Uploaded ? 0 : 2;
            }
        }

        private static string Run(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true });
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o;
        }

        private static int RunCode(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
            p.WaitForExit(); return p.ExitCode;
        }
    }
}
