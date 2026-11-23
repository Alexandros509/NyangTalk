using System;
using System.IO;
using System.Linq;
using NyangTalk.Adapters.Metadata;
using NyangTalk.Shared;

namespace NyangTalk.Tools
{
    /// <summary>
    /// Developer command line for game structure checks (10-SPEC 8장).
    ///   check &lt;gameDir&gt; [fullProfileDir] [--contract file] [--json out.json]   exit 0 ok, 2 problems, 1 error
    ///   dump &lt;dll&gt; &lt;type-substring&gt;                                         list members of matching types
    /// Reads metadata only; never loads or runs game code.
    /// </summary>
    public static class ContractToolProgram
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 3 && args[0] == "dump") return Dump(args[1], args[2]);
                if (args.Length >= 2 && args[0] == "check") return Check(args);
                Console.Error.WriteLine("usage: check <gameDir> [fullProfileDir] [--contract file] [--json out]  |  dump <dll> <type-substring>");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static int Dump(string dll, string filter)
        {
            var a = MetadataReader.Read(dll);
            Console.WriteLine("assembly " + a.Name + " " + a.Version + " refs: " + string.Join(", ", a.References));
            foreach (var t in a.Types.Values.Where(x => x.FullName.IndexOf(filter, StringComparison.Ordinal) >= 0).OrderBy(x => x.FullName))
            {
                Console.WriteLine("type " + t.FullName + (t.IsInterface ? " [interface]" : "") + (t.IsAbstract ? " [abstract]" : "") + " : " + t.BaseType);
                foreach (var f in t.Fields) Console.WriteLine("  field " + (f.IsStatic ? "static " : "") + f.Type + " " + f.Name);
                foreach (var p in t.Properties) Console.WriteLine("  prop  " + (p.IsStatic ? "static " : "") + p.Type + " " + p.Name + (p.Getter != null ? " get" : "") + (p.Setter != null ? " set" : ""));
                foreach (var e in t.Events) Console.WriteLine("  event " + (e.IsStatic ? "static " : "") + e.Type + " " + e.Name);
                foreach (var m in t.Methods) Console.WriteLine("  meth  " + (m.IsStatic ? "static " : "") + m.Return + " " + m.Signature);
            }
            return 0;
        }

        private static int Check(string[] args)
        {
            string game = args[1];
            string full = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : null;
            string contractPath = Arg(args, "--contract") ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contract.json");
            string jsonOut = Arg(args, "--json");
            var report = GameCheck.Run(game, full, File.ReadAllText(contractPath), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Console.WriteLine(report.ToText());
            if (jsonOut != null) File.WriteAllText(jsonOut, report.ToJson());
            return report.HasProblems ? 2 : 0;
        }

        private static string Arg(string[] a, string name)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
