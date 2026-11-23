using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyangTalk.Adapters.GameLink;

namespace NyangTalk.Tests
{
    /// <summary>Fake PC: files, registry and running process for 40-SPEC 5.5.</summary>
    public sealed class FakeLocatorEnv : ILocatorEnv
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Reg = new Dictionary<string, string>();
        public string Running;
        public int Accesses;

        public string RunningGameExe() { return Running; }
        public string Registry(string hive, string key, string name) { string v; return Reg.TryGetValue(hive + "\\" + key + "\\" + name, out v) ? v : null; }
        public bool FileExists(string path) { Accesses++; return Files.ContainsKey(path); }
        public string ReadText(string path) { Accesses++; string v; return Files.TryGetValue(path, out v) ? v : null; }
        public string FinalPath(string dir) { return dir; }

        public void Steam(string steamDir, params string[] libraries)
        {
            Reg[@"HKCU\Software\Valve\Steam\SteamPath"] = steamDir.Replace('\\', '/');
            var vdf = "\"libraryfolders\"\n{\n" + string.Join("", libraries.Select((l, i) => "\t\"" + i + "\"\n\t{\n\t\t\"path\"\t\t\"" + l.Replace("\\", "\\\\") + "\"\n\t}\n")) + "}\n";
            Files[Path.Combine(steamDir, "steamapps", "libraryfolders.vdf")] = vdf;
        }

        public string Game(string library)
        {
            var dir = Path.Combine(library, "steamapps", "common", "BongoCat");
            Files[Path.Combine(library, "steamapps", "appmanifest_3419430.acf")] = "\"AppState\"\n{\n\t\"appid\"\t\t\"3419430\"\n\t\"installdir\"\t\t\"BongoCat\"\n}\n";
            Files[Path.Combine(dir, "BongoCat.exe")] = "x";
            Files[Path.Combine(dir, "BongoCat_Data", "Managed", "Assembly-CSharp.dll")] = "x";
            return dir;
        }

        public void Remove(string dir)
        {
            foreach (var k in Files.Keys.Where(k => k.StartsWith(dir, StringComparison.OrdinalIgnoreCase)).ToList()) Files.Remove(k);
        }
    }

    public static class LocatorTests
    {
        private static GameLocationService Svc(FakeLocatorEnv env, Dictionary<string, string> store)
        {
            return new GameLocationService(new GameLocator(env), () => { string v; return store.TryGetValue("dir", out v) ? v : null; }, v => store["dir"] = v);
        }

        [Test("40-5.5.1")]
        public static void OneLibraryFoundAndSaved()
        {
            var env = new FakeLocatorEnv();
            env.Steam(@"D:\Steam", @"D:\Steam");
            var game = env.Game(@"D:\Steam");
            var store = new Dictionary<string, string>();
            var svc = Svc(env, store);
            string found = null; svc.Found += p => found = p;
            A.Eq(game, svc.OnStart(), "found");
            A.Eq(game, store["dir"], "saved");
            A.Eq(game, found, "one-line notice");
        }

        [Test("40-5.5.2")]
        public static void SecondLibrary()
        {
            var env = new FakeLocatorEnv();
            env.Steam(@"C:\Steam", @"C:\Steam", @"E:\Games\SteamLibrary");
            var game = env.Game(@"E:\Games\SteamLibrary");
            A.Eq(game, Svc(env, new Dictionary<string, string>()).OnStart(), "game in the second library");
            A.Eq(2, GameLocator.ParseLibraryFolders(env.Files[@"C:\Steam\steamapps\libraryfolders.vdf"]).Count, "vdf parsed");
        }

        [Test("40-5.5.3")]
        public static void MovedGameFoundOnFileOperation()
        {
            var env = new FakeLocatorEnv();
            env.Steam(@"C:\Steam", @"C:\Steam", @"F:\Lib");
            var old = env.Game(@"C:\Steam");
            var store = new Dictionary<string, string> { { "dir", old } };
            var svc = Svc(env, store);
            env.Remove(old); env.Files.Remove(@"C:\Steam\steamapps\appmanifest_3419430.acf");
            var moved = env.Game(@"F:\Lib");
            string notice = null; svc.Relocated += p => notice = p;
            A.Eq(moved, svc.ForFileOperation(), "re-found before install");
            A.Eq(moved, store["dir"], "new location saved");
            A.Eq(moved, notice, "notice shown");
        }

        [Test("40-5.5.4")]
        public static void RunningFromElsewhere()
        {
            var env = new FakeLocatorEnv();
            env.Steam(@"C:\Steam", @"C:\Steam");
            var a = env.Game(@"C:\Steam");
            var store = new Dictionary<string, string> { { "dir", @"Z:\Old\BongoCat" } };
            var svc = Svc(env, store);
            env.Running = Path.Combine(a, "BongoCat.exe");
            A.Eq(a, svc.OnGameProcess(env.Running), "re-located from the running game");
            A.Eq(a, store["dir"], "saved");
            A.Eq(a, svc.OnGameProcess(env.Running), "same path: no change");
        }

        [Test("40-5.5.5")]
        public static void NothingFoundKeepsSavedValue()
        {
            var env = new FakeLocatorEnv();
            var store = new Dictionary<string, string> { { "dir", @"Q:\Gone\BongoCat" } };
            var svc = Svc(env, store);
            bool asked = false; svc.NotFound += () => asked = true;
            A.True(svc.ForFileOperation() == null, "no folder to use");
            A.True(asked, "picker shown");
            A.Eq(@"Q:\Gone\BongoCat", store["dir"], "saved location not deleted");
            A.False(svc.Choose(@"Q:\NotAGame"), "picked folder must be a game folder");
        }

        [Test("40-5.5.6")]
        public static void StartDoesNotTouchFiles()
        {
            var env = new FakeLocatorEnv();
            var store = new Dictionary<string, string> { { "dir", @"C:\Games\BongoCat" } };
            var svc = Svc(env, store);
            A.Eq(@"C:\Games\BongoCat", svc.OnStart(), "saved path used");
            A.Eq(0, env.Accesses, "no file access at start");
        }

        [Test("40-5.5.7")]
        public static void SeveralCandidatesListed()
        {
            var env = new FakeLocatorEnv();
            env.Steam(@"C:\Steam", @"C:\Steam", @"D:\Lib");
            var a = env.Game(@"C:\Steam");
            var b = env.Game(@"D:\Lib");
            List<string> shown = null;
            var svc = Svc(env, new Dictionary<string, string>());
            svc.ChooseAmong += l => shown = l;
            svc.OnStart();
            A.True(shown != null && shown.Contains(a) && shown.Contains(b), "both offered");
        }

        [Test("40-5.3")]
        public static void RealPcSearchFindsInstalledGame()
        {
            if (!File.Exists(Path.Combine(GameCheckTests.RealGame, "BongoCat.exe"))) return;
            var r = new GameLocator(new RealLocatorEnv()).Locate();   // reads registry and Steam files only
            A.Eq(LocateKind.Found, r.Kind, "found on this PC");
            A.True(string.Equals(r.Path.TrimEnd('\\'), GameCheckTests.RealGame, StringComparison.OrdinalIgnoreCase), "path " + r.Path);
        }
    }
}
