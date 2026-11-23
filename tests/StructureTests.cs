using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NyangTalk.Tests
{
    public static class StructureTests
    {
        /// <summary>01-OVERVIEW 2.1: the domain layer has no I/O, clock, UI or adapter dependencies.</summary>
        [Test("01-2.1")]
        public static void DomainIsPure()
        {
            var forbidden = new[] { @"\bSystem\.IO\b", @"\bFile\.", @"\bDirectory\.", @"DateTime\.(Utc)?Now", @"\bProcess\b", @"Thread\.Sleep",
                @"System\.Windows", @"System\.Drawing", @"System\.Net", @"NyangTalk\.Adapters", @"NyangTalk\.Application", @"NyangTalk\.App\b", @"Environment\." };
            var bad = Directory.GetFiles(Path.Combine(Ctx.Root, "src", "Domain"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => forbidden.Where(p => Regex.IsMatch(File.ReadAllText(f), p)).Select(p => Path.GetFileName(f) + " uses " + p)).ToList();
            A.True(bad.Count == 0, string.Join("; ", bad));
            var refs = Assembly.LoadFrom(Path.Combine(Ctx.Root, "build", "NyangTalk.Domain.dll")).GetReferencedAssemblies().Select(a => a.Name).OrderBy(n => n).ToList();
            A.True(refs.All(n => n == "mscorlib" || n == "System" || n == "System.Core"), "domain references: " + string.Join(",", refs));
        }

        /// <summary>The nine bounded contexts exist as domain folders.</summary>
        [Test("01-2.1")]
        public static void BoundedContextsExist()
        {
            foreach (var c in new[] { "Conversation", "Sending", "Lobby", "People", "Appearance", "GameLink", "Automation", "Update", "Preferences" })
                A.True(Directory.Exists(Path.Combine(Ctx.Root, "src", "Domain", c)), "context folder " + c);
        }

        /// <summary>Application must not reference adapters or UI.</summary>
        [Test("01-2.1")]
        public static void ApplicationLayerDirection()
        {
            var refs = Assembly.LoadFrom(Path.Combine(Ctx.Root, "build", "NyangTalk.Application.dll")).GetReferencedAssemblies().Select(a => a.Name).ToList();
            A.False(refs.Any(n => n.Contains("Adapters") || n.Contains("Windows.Forms") || n.Contains("Drawing")), "application references: " + string.Join(",", refs));
        }

        /// <summary>Product scope: no "typing" indicator or "hide/block person" feature anywhere (01-OVERVIEW 3장 3번, 60-SPEC 5.9).</summary>
        [Test("60-4.8,01-3")]
        public static void ForbiddenFeaturesAbsent()
        {
            var patterns = new[] { @"(?i)\btyping\b", "작성 중", "入力中", @"(?i)\bblock(ed)?User", @"(?i)hidePerson", @"(?i)mute(d)?User", "차단", "숨기기\\s*\\(사람", "ブロック",
                // 60-SPEC 4장 8번 (2026-11-19): join/leave notices and opening Steam profiles were removed
                @"(?i)presence", "들어왔어요", "나갔어요", "入室", "退室", @"(?i)\bjoined\b", @"(?i)steamcommunity", @"(?i)OpenProfile", "프로필 열기" };
            var files = Directory.GetFiles(Path.Combine(Ctx.Root, "src"), "*.cs", SearchOption.AllDirectories);
            var hits = files.SelectMany(f => patterns.Where(p => Regex.IsMatch(File.ReadAllText(f), p)).Select(p => Path.GetFileName(f) + ":" + p)).ToList();
            A.True(hits.Count == 0, "forbidden feature words: " + string.Join("; ", hits));
        }
    }
}
