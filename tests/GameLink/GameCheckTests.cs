using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CSharp;
using NyangTalk.Adapters.GameLink;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Adapters.Storage;
using NyangTalk.Domain.GameLink;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public static class GameCheckTests
    {
        public const string RealGame = @"C:\Program Files (x86)\Steam\steamapps\common\BongoCat";
        private static bool HaveGame { get { return File.Exists(Path.Combine(RealGame, "BongoCat_Data", "Managed", "Assembly-CSharp.dll")); } }

        /// <summary>Compiles C# source into a fake game folder (BongoCat_Data\Managed\Assembly-CSharp.dll).</summary>
        public static string FakeGame(string tag, string assemblyCSharpSource)
        {
            var dir = Ctx.NewDir("fakegame-" + tag);
            var managed = Path.Combine(dir, "BongoCat_Data", "Managed");
            Directory.CreateDirectory(managed);
            File.WriteAllText(Path.Combine(dir, "BongoCat_Data", "globalgamemanagers"), "ggm-" + tag);
            using (var p = new CSharpCodeProvider())
            {
                var opt = new CompilerParameters { OutputAssembly = Path.Combine(managed, "Assembly-CSharp.dll"), GenerateExecutable = false };
                var res = p.CompileAssemblyFromSource(opt, assemblyCSharpSource);
                if (res.Errors.HasErrors) throw new Exception(string.Join("; ", res.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }
            return dir;
        }

        private const string V1 = "namespace BongoCat.Multiplayer { public class SteamMultiplayer { public static SteamMultiplayer Instance; public static void SendSpeech(string s) {} public int Count(string a, int b) { return 0; } } }";

        private static string Contract(params string[] entries)
        {
            return "{\"contract_version\":7,\"entries\":[" + string.Join(",", entries) + "]}";
        }

        private const string SendEntry = "{\"id\":\"send\",\"area\":\"ChatSend\",\"assembly\":\"Assembly-CSharp\",\"type\":[\"BongoCat.Multiplayer.SteamMultiplayer\"],\"kind\":\"method\",\"name\":[\"SendSpeech\"],\"returns\":\"System.Void\",\"params\":[\"System.String\"],\"static\":true}";
        private const string InstEntry = "{\"id\":\"inst\",\"area\":\"ChatReceive\",\"assembly\":\"Assembly-CSharp\",\"type\":[\"BongoCat.Multiplayer.SteamMultiplayer\"],\"kind\":\"field\",\"name\":[\"Instance\"],\"returns\":\"BongoCat.Multiplayer.SteamMultiplayer\",\"static\":true}";

        private static EntryVerdict VerdictFor(string source, string entry)
        {
            var g = FakeGame("v", source);
            var r = GameCheck.Run(g, null, Contract(entry), null);
            return r.Entries.Single().Verdict;
        }

        [Test("10-11.5")]
        public static void SyntheticChangesJudgedExactly()
        {
            A.Eq(EntryVerdict.Match, VerdictFor(V1, SendEntry), "unchanged");
            A.Eq(EntryVerdict.Missing, VerdictFor(V1.Replace("public static void SendSpeech(string s) {}", ""), SendEntry), "member deleted");
            A.Eq(EntryVerdict.Missing, VerdictFor(V1.Replace("SendSpeech", "SendChat"), SendEntry), "member renamed");
            A.Eq(EntryVerdict.SignatureDiffers, VerdictFor(V1.Replace("SendSpeech(string s)", "SendSpeech(int s)"), SendEntry), "parameter type changed");
            A.Eq(EntryVerdict.SignatureDiffers, VerdictFor(V1.Replace("public static void SendSpeech(string s) {}", "public static bool SendSpeech(string s) { return true; }"), SendEntry), "return type changed");
            A.Eq(EntryVerdict.Missing, VerdictFor(V1.Replace("namespace BongoCat.Multiplayer", "namespace BongoCat.Net"), SendEntry), "type moved to another namespace");
            A.Eq(EntryVerdict.SignatureDiffers, VerdictFor(V1.Replace("public static SteamMultiplayer Instance", "public SteamMultiplayer Instance"), InstEntry), "static changed");
        }

        [Test("10-11.6")]
        public static void AlternateNameRecorded()
        {
            var entry = SendEntry.Replace("\"name\":[\"SendSpeech\"]", "\"name\":[\"SendSpeech\",\"SendChat\"]");
            var g = FakeGame("alt", V1.Replace("SendSpeech", "SendChat"));
            var r = GameCheck.Run(g, null, Contract(entry), null);
            var e = r.Entries.Single();
            A.Eq(EntryVerdict.MatchByAlternate, e.Verdict, "alternate verdict");
            A.Eq("SendChat", e.UsedName, "used candidate recorded");
            A.True(r.ToJson().Contains("\"used_name\":\"SendChat\""), "in JSON report");
            A.True(r.Areas["ChatSend"], "area works through the alternate");
        }

        [Test("10-11.7,10-7")]
        public static void BrokenSendAreaOnly()
        {
            var g = FakeGame("area", V1.Replace("SendSpeech", "Gone"));
            var r = GameCheck.Run(g, null, Contract(SendEntry, InstEntry), null);
            A.False(r.Areas["ChatSend"], "send broken");
            A.True(r.Areas["ChatReceive"], "receive fine");
            A.True(r.HasProblems, "exit code 2 case");
        }

        [Test("10-11.8")]
        public static void SameFingerprintSkipsRecheck()
        {
            var g = FakeGame("fp", V1);
            var docs = new JsonDocumentStore(Ctx.NewDir("fpdocs"));
            var svc = new GameLinkService(docs, Contract(SendEntry), null);
            var areas = new AreaAvailability();
            A.True(svc.Refresh(g, null, areas), "first run checks");
            long metaBefore = MetadataReader.BytesRead, hashBefore = GameCheck.HashedBytes;
            var svc2 = new GameLinkService(docs, Contract(SendEntry), null);
            A.False(svc2.Refresh(g, null, areas), "same fingerprint: no re-check");
            A.Eq(metaBefore, MetadataReader.BytesRead, "no metadata read");
            A.Eq(hashBefore, GameCheck.HashedBytes, "no hashing (size/time unchanged)");
            A.True(areas.IsAvailable(FeatureArea.ChatSend), "cached verdict applied");
            System.Threading.Thread.Sleep(20);
            File.AppendAllText(Path.Combine(g, "BongoCat_Data", "globalgamemanagers"), "update");
            A.True(svc2.Refresh(g, null, areas), "fingerprint changed: re-check once");
            A.False(svc2.Refresh(g, null, areas), "and not again");
        }

        [Test("10-11.8,10-7")]
        public static void CachedCheckKeepsMixingProblems()
        {
            var g = FakeGame("mixcache", "namespace G { public class X { } }");
            var managed = GameCheck.ManagedDir(g);
            Compile(Path.Combine(managed, "Lite.dll"), "namespace L { public interface I { void A(); } }", null);
            Compile(Path.Combine(managed, "Newtonsoft.Json.dll"), "namespace N { public class J : L.I { public void A() { } } }", Path.Combine(managed, "Lite.dll"));
            var full = Ctx.NewDir("mixcachefull");
            Compile(Path.Combine(full, "Lite.dll"), "namespace L { public interface I { void A(); void B(); } }", null);
            var docs = new JsonDocumentStore(Ctx.NewDir("mixcachedocs"));
            var svc = new GameLinkService(docs, Contract(SendEntry), null);
            svc.Refresh(g, full, null);
            int first = svc.LastReport.Mix.Count;
            A.True(first > 0, "mixing found on first run");
            var svc2 = new GameLinkService(docs, Contract(SendEntry), null);
            A.False(svc2.Refresh(g, full, null), "cached path");
            A.Eq(first, svc2.LastReport.Mix.Count, "cached report still has the mixing problems");
            A.True(svc2.LastReport.Mix[0].Fixable, "fixable flag kept");
        }

        private static void Compile(string output, string source, string reference)
        {
            using (var p = new CSharpCodeProvider())
            {
                var opt = new CompilerParameters { OutputAssembly = output, GenerateExecutable = false };
                if (reference != null) opt.ReferencedAssemblies.Add(reference);
                var res = p.CompileAssemblyFromSource(opt, source);
                if (res.Errors.HasErrors) throw new Exception(res.Errors[0].ErrorText);
            }
        }

        [Test("10-11.1,10-11.10")]
        public static void RealGameNoModsZeroProblems()
        {
            if (!HaveGame) { Ctx.Measurements.Add("10-11.1: 게임 폴더 없음 — 실행하지 않음"); return; }
            var empty = Ctx.NewDir("emptyfull");
            var r = GameCheck.Run(RealGame, empty, File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json")), null);
            A.Eq(0, r.Mix.Count, "no false positives without mods: " + string.Join("; ", r.Mix.Take(3)));
            A.True(r.Areas.Values.All(v => v), "all areas match current game build");
            Ctx.Measurements.Add("게임 구조 검사(실제 게임 파일 읽기 전용): 어셈블리 " + r.AssembliesRead + "개, 타입 " + r.TypesRead + "개, " + r.ElapsedMs + " ms (목표 3,000 ms), 혼용 문제 " + r.Mix.Count + "건");
            A.True(r.ElapsedMs < 3000, "under 3 s");
        }

        /// <summary>
        /// 10-11.3 approximation: the Unity-profile full mscorlib is not available here, so the .NET Framework mscorlib
        /// stands in. It shows the checker catches this class of problem; the real case stays on the manual checklist.
        /// </summary>
        [Test("10-11.3(근사)")]
        public static void FullMscorlibOnlyFlagsPipes()
        {
            if (!HaveGame) return;
            var full = Ctx.NewDir("fullcorlib");
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll"), Path.Combine(full, "mscorlib.dll"));
            var r = GameCheck.Run(RealGame, full, File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json")), null);
            A.True(r.Mix.Any(m => m.Type == "System.IO.Pipes.NamedPipeServerStream"), "NamedPipeServerStream flagged");
            A.False(r.Mix.First(m => m.Type == "System.IO.Pipes.NamedPipeServerStream").Fixable, "System.Core is not fixable by us");
        }

        [Test("10-11.9")]
        public static void PluginUsesOnlyContractNames()
        {
            var contract = ContractFile.Parse(File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json")));
            var ids = new HashSet<string>(contract.Entries.Select(e => e.Id));
            var bad = new List<string>();
            int used = 0;
            foreach (var f in Directory.GetFiles(Path.Combine(Ctx.Root, "src", "Plugin"), "*.cs"))
            {
                var src = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(src, "(?:_c\\.(?:Get|Call|CallWithArgs|Has|TypeOf)\\(|add\\(|, )\"([a-zA-Z]+\\.[a-zA-Z]+)\""))
                {
                    used++;
                    if (System.Text.RegularExpressions.Regex.IsMatch(m.Groups[1].Value, @"\.(json|png|ndjson|key)$")) continue; // file names, not contract ids
                    if (!ids.Contains(m.Groups[1].Value)) bad.Add(Path.GetFileName(f) + ": unknown contract id " + m.Groups[1].Value);
                }
                foreach (Match m in Regex.Matches(src, "\\.(GetField|GetProperty|GetMethod|GetEvent|GetType)\\(\"([^\"]+)\""))
                    bad.Add(Path.GetFileName(f) + ": literal reflection target " + m.Groups[2].Value);
                foreach (Match m in Regex.Matches(src, "MakeAction\\(this, \"([^\"]+)\""))
                    if (!Regex.IsMatch(src, "void " + m.Groups[1].Value + "<")) bad.Add("handler " + m.Groups[1].Value + " is not a plugin method");
            }
            A.True(used >= 20, "plugin uses contract ids (" + used + ")");
            A.True(bad.Count == 0, string.Join("; ", bad));
        }

        [Test("40-1.1,10-5.2")]
        public static void PluginReferencesExistInGameLibraries()
        {
            var dll = Path.Combine(Ctx.Root, "build", "NyangTalk.GameLink.dll");
            if (!HaveGame || !File.Exists(dll)) { Ctx.Measurements.Add("플러그인 참조 검사: 게임 또는 플러그인 빌드 없음 — 실행하지 않음"); return; }
            var world = new GameCheck.World(GameCheck.ManagedDir(RealGame), Path.Combine(RealGame, "BepInEx", "core"));
            // Assembly-level attributes the C# compiler always emits; Mono resolves them only when they are reflected on.
            // Listed on the manual checklist (plugin must load in the real game).
            var compilerEmitted = new[] { "type mscorlib:System.Diagnostics.DebuggableAttribute", "type mscorlib:System.Runtime.CompilerServices.CompilationRelaxationsAttribute" };
            var missing = GameCheck.MissingReferences(world, MetadataReader.Read(dll)).Where(m => !compilerEmitted.Contains(m)).ToList();
            A.True(missing.Count == 0, "plugin uses members the game's stripped libraries lack: " + string.Join("; ", missing.Take(8)));
        }
    }
}
