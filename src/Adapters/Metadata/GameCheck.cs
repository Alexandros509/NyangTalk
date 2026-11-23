using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NyangTalk.Domain.GameLink;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.Metadata
{
    public enum EntryVerdict { Match, MatchByAlternate, SignatureDiffers, Missing, AssemblyMissing }

    public sealed class EntryResult
    {
        public ContractEntry Entry;
        public EntryVerdict Verdict;
        public string UsedType, UsedName, Detail;
    }

    public sealed class MixProblem
    {
        public string Assembly, Type, Member, RequiredBy;
        public bool Fixable;
        public override string ToString() { return Assembly + " " + Type + " :: " + Member + " (required by " + RequiredBy + ")" + (Fixable ? " [fixable]" : ""); }
    }

    public sealed class CheckReport
    {
        public GameFingerprint Fingerprint;
        public int ContractVersion;
        public readonly List<EntryResult> Entries = new List<EntryResult>();
        public readonly List<MixProblem> Mix = new List<MixProblem>();
        public readonly Dictionary<string, bool> Areas = new Dictionary<string, bool>();
        public long ElapsedMs;
        public int AssembliesRead, TypesRead;
        public string UserProfile;

        public bool HasProblems { get { return Mix.Count > 0 || Entries.Any(e => e.Entry.Required && (e.Verdict != EntryVerdict.Match && e.Verdict != EntryVerdict.MatchByAlternate)); } }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NyangTalk game structure check");
            sb.AppendLine("fingerprint: " + (Fingerprint == null ? "-" : Fingerprint.Id) + "  unity: " + (Fingerprint == null ? "-" : Fingerprint.UnityPlayerVersion));
            sb.AppendLine("contract version: " + ContractVersion + "  assemblies: " + AssembliesRead + "  types: " + TypesRead + "  time: " + ElapsedMs + " ms");
            sb.AppendLine();
            sb.AppendLine("| area | entry | verdict | detail |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var e in Entries)
                sb.AppendLine("| " + e.Entry.Area + " | " + e.Entry.Id + (e.Entry.Required ? "" : " (optional)") + " | " + e.Verdict + " | " + Mask(e.Detail ?? (e.UsedType + "." + e.UsedName)) + " |");
            sb.AppendLine();
            foreach (var a in Areas) sb.AppendLine("area " + a.Key + ": " + (a.Value ? "ok" : "BROKEN"));
            sb.AppendLine();
            sb.AppendLine("mixing problems: " + Mix.Count);
            foreach (var m in Mix) sb.AppendLine("  " + m);
            return sb.ToString();
        }

        private string Mask(string s) { return NyangTalk.Domain.Preferences.DiagnosticsSanitizer.MaskUserPath(s, UserProfile); }

        public string ToJson()
        {
            var d = new Dictionary<string, object>();
            d["fingerprint"] = Fingerprint == null ? null : Fingerprint.Id;
            d["unity_player"] = Fingerprint == null ? null : Fingerprint.UnityPlayerVersion;
            d["contract_version"] = (long)ContractVersion;
            d["elapsed_ms"] = ElapsedMs;
            d["entries"] = Entries.Select(e => (object)new Dictionary<string, object> { { "id", e.Entry.Id }, { "area", e.Entry.Area }, { "required", e.Entry.Required }, { "verdict", e.Verdict.ToString() }, { "used_type", e.UsedType }, { "used_name", e.UsedName }, { "detail", e.Detail } }).ToList();
            d["areas"] = Areas.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            d["mixing"] = Mix.Select(m => (object)new Dictionary<string, object> { { "assembly", m.Assembly }, { "type", m.Type }, { "member", m.Member }, { "required_by", m.RequiredBy }, { "fixable", m.Fixable } }).ToList();
            return Json.Write(d);
        }
    }

    /// <summary>
    /// Static game checks (10-SPEC 5장): 5.1 contract entries against game metadata, 5.2 library mixing when a
    /// full-profile folder replaces the game's stripped libraries. Reads files only.
    /// </summary>
    public static class GameCheck
    {
        /// <summary>Public libraries we may ship in full form, so mixing problems inside them are fixable (40-SPEC 1.1).</summary>
        public static readonly string[] PublicLibraries = { "Newtonsoft.Json" };

        public static string ManagedDir(string gameDir) { return Path.Combine(gameDir, "BongoCat_Data", "Managed"); }

        public static CheckReport Run(string gameDir, string fullProfileDir, string contractJson, string userProfile)
        {
            var sw = Stopwatch.StartNew();
            var report = new CheckReport { UserProfile = userProfile };
            var contract = ContractFile.Parse(contractJson);
            report.ContractVersion = contract.Version;
            report.Fingerprint = Fingerprint(gameDir, null);
            var world = new World(ManagedDir(gameDir), fullProfileDir);
            CheckContract(world, contract, report);
            if (!string.IsNullOrEmpty(fullProfileDir)) report.Mix.AddRange(CheckMixing(world));
            report.AssembliesRead = world.Loaded.Count;
            report.TypesRead = world.Loaded.Values.Where(a => a != null).Sum(a => a.Types.Count);
            report.ElapsedMs = sw.ElapsedMilliseconds;
            return report;
        }

        // ------------------------------------------------------------ fingerprint (10-SPEC 3장)

        public static FileStamp Stamp(string path, FileStamp previous)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            var s = new FileStamp { Size = fi.Length, MtimeTicks = fi.LastWriteTimeUtc.Ticks };
            if (previous != null && previous.SameStat(s) && previous.Sha256 != null) { s.Sha256 = previous.Sha256; return s; }
            s.Sha256 = Sha256File(path);
            return s;
        }

        public static long HashedBytes;

        public static string Sha256File(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                HashedBytes += fs.Length;
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
        }

        public static GameFingerprint Fingerprint(string gameDir, GameFingerprint previous)
        {
            var data = Path.Combine(gameDir, "BongoCat_Data");
            var fp = new GameFingerprint
            {
                Assembly = Stamp(Path.Combine(data, "Managed", "Assembly-CSharp.dll"), previous == null ? null : previous.Assembly),
                GlobalGameManagers = Stamp(Path.Combine(data, "globalgamemanagers"), previous == null ? null : previous.GlobalGameManagers)
            };
            var up = Path.Combine(gameDir, "UnityPlayer.dll");
            if (File.Exists(up)) fp.UnityPlayerVersion = FileVersionInfo.GetVersionInfo(up).ProductVersion;
            return fp;
        }

        // ------------------------------------------------------------ assembly world

        public sealed class World
        {
            private readonly string _managed, _full;
            public readonly Dictionary<string, AssemblyM> Loaded = new Dictionary<string, AssemblyM>(StringComparer.OrdinalIgnoreCase);

            public World(string managed, string full) { _managed = managed; _full = full; }

            /// <summary>Mono's order when a full-profile folder is configured: that folder first, then the game folder.</summary>
            public AssemblyM Get(string name)
            {
                AssemblyM a;
                if (Loaded.TryGetValue(name, out a)) return a;
                string path = null;
                if (!string.IsNullOrEmpty(_full) && File.Exists(Path.Combine(_full, name + ".dll"))) path = Path.Combine(_full, name + ".dll");
                else if (File.Exists(Path.Combine(_managed, name + ".dll"))) path = Path.Combine(_managed, name + ".dll");
                try { a = path == null ? null : MetadataReader.Read(path); }
                catch (BadImageFormatException) { a = null; }
                Loaded[name] = a;
                return a;
            }

            public IEnumerable<string> GameAssemblyNames()
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(_managed, "*.dll")) names.Add(Path.GetFileNameWithoutExtension(f));
                if (!string.IsNullOrEmpty(_full) && Directory.Exists(_full)) foreach (var f in Directory.GetFiles(_full, "*.dll")) names.Add(Path.GetFileNameWithoutExtension(f));
                return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>Finds a type by name starting at an assembly and following type forwarders.</summary>
            public TypeInfoM Resolve(string assembly, string definition)
            {
                for (int hop = 0; hop < 8 && assembly != null; hop++)
                {
                    var a = Get(assembly);
                    if (a == null) return null;
                    TypeInfoM t;
                    if (a.Types.TryGetValue(definition, out t)) return t;
                    string fwd;
                    var outer = definition.Split('+')[0];
                    if (a.Forwarders.TryGetValue(outer, out fwd)) { assembly = fwd; continue; }
                    return null;
                }
                return null;
            }

            public TypeInfoM Resolve(TypeName n) { return n == null || n.FullName.StartsWith("!") ? null : Resolve(n.Assembly, n.Definition); }
        }

        // ------------------------------------------------------------ 5.1 contract

        public static void CheckContract(World world, ContractFile contract, CheckReport report)
        {
            foreach (var e in contract.Entries)
            {
                var r = new EntryResult { Entry = e };
                if (world.Get(e.Assembly) == null) { r.Verdict = EntryVerdict.AssemblyMissing; r.Detail = e.Assembly + ".dll not found"; report.Entries.Add(r); continue; }
                EntryResult best = null;
                for (int ti = 0; ti < e.Types.Count && (best == null || best.Verdict != EntryVerdict.Match && best.Verdict != EntryVerdict.MatchByAlternate); ti++)
                {
                    var t = world.Resolve(e.Assembly, e.Types[ti]);
                    if (t == null) continue;
                    if (e.Kind == "type") { best = new EntryResult { Entry = e, Verdict = ti == 0 ? EntryVerdict.Match : EntryVerdict.MatchByAlternate, UsedType = t.FullName }; break; }
                    for (int ni = 0; ni < e.Names.Count; ni++)
                    {
                        string detail;
                        var found = FindMember(t, e, e.Names[ni], out detail);
                        // members declared on a base class (static Instance of a generic singleton, …)
                        for (var b = t; found == null && e.Inherited && b != null && b.BaseType != null; )
                        {
                            b = world.Resolve(b.BaseType);
                            if (b != null) found = FindMember(b, e, e.Names[ni], out detail);
                        }
                        bool alt = ti > 0 || ni > 0;
                        if (found == true) { best = new EntryResult { Entry = e, Verdict = alt ? EntryVerdict.MatchByAlternate : EntryVerdict.Match, UsedType = t.FullName, UsedName = e.Names[ni] }; break; }
                        if (found == false && best == null) best = new EntryResult { Entry = e, Verdict = EntryVerdict.SignatureDiffers, UsedType = t.FullName, UsedName = e.Names[ni], Detail = detail };
                    }
                }
                if (best == null) best = new EntryResult { Entry = e, Verdict = EntryVerdict.Missing, Detail = "no candidate type/member found" };
                report.Entries.Add(best);
            }
            foreach (var area in ContractFile.Areas)
                report.Areas[area] = report.Entries.Where(x => x.Entry.Area == area && x.Entry.Required).All(x => x.Verdict == EntryVerdict.Match || x.Verdict == EntryVerdict.MatchByAlternate);
        }

        /// <summary>null: no member with that name; true: matches; false: exists but the shape differs.</summary>
        private static bool? FindMember(TypeInfoM t, ContractEntry e, string name, out string detail)
        {
            detail = null;
            switch (e.Kind)
            {
                case "field":
                    {
                        var f = t.Fields.FirstOrDefault(x => x.Name == name);
                        if (f == null) return null;
                        if (f.Type != e.Returns) { detail = "type " + f.Type + " ≠ " + e.Returns; return false; }
                        if (f.IsStatic != e.Static) { detail = "static " + f.IsStatic + " ≠ " + e.Static; return false; }
                        return true;
                    }
                case "property":
                    {
                        var p = t.Properties.FirstOrDefault(x => x.Name == name);
                        if (p == null) return null;
                        if (p.Type != e.Returns) { detail = "type " + p.Type + " ≠ " + e.Returns; return false; }
                        if (p.IsStatic != e.Static) { detail = "static " + p.IsStatic + " ≠ " + e.Static; return false; }
                        return true;
                    }
                case "event":
                    {
                        var ev = t.Events.FirstOrDefault(x => x.Name == name);
                        if (ev == null) return null;
                        if (e.Returns != null && ev.Type != e.Returns) { detail = "type " + ev.Type + " ≠ " + e.Returns; return false; }
                        return true;
                    }
                default:
                    {
                        var all = t.Methods.Where(x => x.Name == name).ToList();
                        if (all.Count == 0) return null;
                        var exact = all.FirstOrDefault(m => m.Params.SequenceEqual(e.Params) && m.Return == e.Returns && m.IsStatic == e.Static);
                        if (exact != null) return true;
                        var m0 = all[0];
                        detail = m0.Return + " " + m0.Signature + (m0.IsStatic ? " static" : "") + " ≠ " + e.Returns + " " + name + "(" + string.Join(",", e.Params) + ")" + (e.Static ? " static" : "");
                        return false;
                    }
            }
        }

        // ------------------------------------------------------------ 5.2 library mixing

        public static List<MixProblem> CheckMixing(World world)
        {
            var problems = new List<MixProblem>();
            var seen = new HashSet<string>();
            foreach (var name in world.GameAssemblyNames())
            {
                var a = world.Get(name);
                if (a == null) continue;
                foreach (var t in a.Types.Values)
                {
                    if (t.IsInterface) continue;
                    // (1) every interface member implemented
                    foreach (var itfName in t.Interfaces)
                    {
                        var itf = world.Resolve(itfName);
                        if (itf == null) continue;
                        foreach (var m in itf.Methods.Where(x => x.IsAbstract && !x.IsStatic))
                        {
                            if (Implements(world, t, itf, m)) continue;
                            Add(problems, seen, a.Name, t.FullName, itf.FullName + "." + m.Name + "/" + m.Params.Count, itf.Assembly);
                        }
                    }
                    // (2) abstract members of abstract bases overridden
                    if (!t.IsAbstract)
                    {
                        var chain = BaseChain(world, t);
                        foreach (var b in chain.Where(x => x.IsAbstract))
                            foreach (var m in b.Methods.Where(x => x.IsAbstract && !x.IsStatic))
                            {
                                if (new[] { t }.Concat(chain.TakeWhile(x => x != b)).Any(x => x.Methods.Any(y => y.Name == m.Name && y.Params.Count == m.Params.Count && !y.IsAbstract))) continue;
                                if (t.ExplicitImpls.Any(x => x.EndsWith("." + m.Name))) continue;
                                Add(problems, seen, a.Name, t.FullName, b.FullName + "." + m.Name + "/" + m.Params.Count, b.Assembly);
                            }
                    }
                }
                // (3) members this assembly's code references exist in the libraries it will actually get
                foreach (var mr in a.MemberRefs)
                {
                    if (mr.Parent.IsArray || mr.Parent.FullName.StartsWith("!")) continue; // runtime-provided array members, generic parameters
                    var target = world.Resolve(mr.Parent);
                    if (target == null) continue;
                    if (HasMember(world, target, mr)) continue;
                    Add(problems, seen, target.Assembly, target.FullName, mr.Name + (mr.IsField ? "" : "/" + mr.Params.Count), a.Name);
                }
            }
            return problems;
        }

        /// <summary>Members an assembly (e.g. our plugin) references that the given world does not have.</summary>
        public static List<string> MissingReferences(World world, AssemblyM a)
        {
            var r = new List<string>();
            foreach (var mr in a.MemberRefs)
            {
                if (mr.Parent.IsArray || mr.Parent.FullName.StartsWith("!")) continue;
                var target = world.Resolve(mr.Parent);
                if (target == null) { r.Add("type " + mr.Parent.Assembly + ":" + mr.Parent.Definition); continue; }
                if (!HasMember(world, target, mr)) r.Add(target.Assembly + ":" + target.FullName + "::" + mr.Name + (mr.IsField ? "" : "/" + mr.Params.Count));
            }
            return r.Distinct().ToList();
        }

        private static void Add(List<MixProblem> list, HashSet<string> seen, string asm, string type, string member, string requiredBy)
        {
            var key = asm + "|" + type + "|" + member;
            if (!seen.Add(key)) return;
            list.Add(new MixProblem { Assembly = asm, Type = type, Member = member, RequiredBy = requiredBy, Fixable = PublicLibraries.Contains(asm, StringComparer.OrdinalIgnoreCase) });
        }

        private static List<TypeInfoM> BaseChain(World world, TypeInfoM t)
        {
            var r = new List<TypeInfoM>();
            var cur = t;
            for (int i = 0; i < 32 && cur != null && cur.BaseType != null; i++)
            {
                cur = world.Resolve(cur.BaseType);
                if (cur != null) r.Add(cur);
            }
            return r;
        }

        private static bool Implements(World world, TypeInfoM t, TypeInfoM itf, MethodInfoM m)
        {
            var types = new[] { t }.Concat(BaseChain(world, t)).ToList();
            foreach (var x in types)
            {
                if (x.Methods.Any(y => !y.IsStatic && y.Params.Count == m.Params.Count && (y.Name == m.Name || y.Name.EndsWith("." + m.Name)))) return true;
                if (x.ExplicitImpls.Any(n => n.EndsWith("." + m.Name))) return true;
            }
            return false;
        }

        private static bool HasMember(World world, TypeInfoM t, MemberRefM mr)
        {
            var cur = t;
            for (int i = 0; i < 32 && cur != null; i++)
            {
                if (mr.IsField ? cur.Fields.Any(f => f.Name == mr.Name) : cur.Methods.Any(m => m.Name == mr.Name && m.Params.Count == mr.Params.Count)) return true;
                // interfaces: members referenced through an interface type are declared on the interface itself or its bases
                foreach (var itf in cur.Interfaces)
                {
                    var it = world.Resolve(itf);
                    if (it != null && !mr.IsField && it.Methods.Any(m => m.Name == mr.Name && m.Params.Count == mr.Params.Count)) return true;
                }
                cur = cur.BaseType == null ? null : world.Resolve(cur.BaseType);
            }
            return false;
        }
    }
}
