using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NyangTalk.Tests
{
    /// <summary>Marks a check. <c>Spec</c> names the acceptance item it covers, e.g. "50-9.3".</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
        public string Spec;
        public TestAttribute() { }
        public TestAttribute(string spec) { Spec = spec; }
    }

    public sealed class AssertFailed : Exception { public AssertFailed(string m) : base(m) { } }

    public static class A
    {
        public static void True(bool c, string what) { if (!c) throw new AssertFailed(what); }
        public static void False(bool c, string what) { if (c) throw new AssertFailed(what); }
        public static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertFailed(what + ": expected <" + expected + "> got <" + actual + ">");
        }
        public static void Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); } catch (T) { return; }
            throw new AssertFailed(what + ": expected " + typeof(T).Name);
        }
    }

    public static class Ctx
    {
        public static string Root;
        public static string Scratch;
        public static readonly List<string> Measurements = new List<string>();
        public static string NewDir(string name)
        {
            var d = Path.Combine(Scratch, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // a drawing error must fail the check, not open WinForms' error dialog on the user's screen
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException);
            Ctx.Root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            string filter = args.Length > 1 ? args[1] : null;
            Ctx.Scratch = Path.Combine(Ctx.Root, "build", "test-scratch");
            if (Directory.Exists(Ctx.Scratch)) { try { Directory.Delete(Ctx.Scratch, true); } catch (IOException) { } }
            Directory.CreateDirectory(Ctx.Scratch);

            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
            int pass = 0, fail = 0;
            var specs = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var failures = new List<string>();
            foreach (var t in tests)
            {
                var name = t.DeclaringType.Name + "." + t.Name;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var attr = (TestAttribute)t.GetCustomAttributes(typeof(TestAttribute), false)[0];
                var sw = Stopwatch.StartNew();
                string result;
                try
                {
                    t.Invoke(null, null);
                    pass++;
                    result = "PASS";
                }
                catch (TargetInvocationException ex)
                {
                    fail++;
                    var inner = ex.InnerException;
                    result = "FAIL";
                    failures.Add(name + ": " + (inner is AssertFailed ? inner.Message : inner.ToString()));
                }
                Console.WriteLine(result + "  " + name + "  (" + sw.ElapsedMilliseconds + " ms)" + (attr.Spec != null ? "  [" + attr.Spec + "]" : ""));
                if (attr.Spec != null)
                {
                    foreach (var s in attr.Spec.Split(','))
                    {
                        List<string> l;
                        if (!specs.TryGetValue(s.Trim(), out l)) specs[s.Trim()] = l = new List<string>();
                        l.Add(result + " " + name);
                    }
                }
            }
            Console.WriteLine();
            foreach (var f in failures) Console.WriteLine("FAILED " + f);
            Console.WriteLine("passed " + pass + ", failed " + fail);

            // Coverage table for docs/acceptance-status.md (generated, not hand-written).
            var report = new List<string> { "# 수락 검사 자동 결과", "", "`build.ps1` 실행 때 자동으로 생성된다. 손으로 고치지 않는다.", "",
                "생성: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · 통과 " + pass + " · 실패 " + fail, "",
                "| 명세 항목 | 검사 |", "|---|---|" };
            foreach (var kv in specs) report.Add("| " + kv.Key + " | " + string.Join("<br>", kv.Value) + " |");
            if (Ctx.Measurements.Count > 0)
            {
                report.Add(""); report.Add("## 측정값 (이 개발 PC에서 측정, 다른 PC 결과 아님)"); report.Add("");
                foreach (var m in Ctx.Measurements) report.Add("- " + m);
            }
            if (filter == null) File.WriteAllLines(Path.Combine(Ctx.Root, "docs", "acceptance-status.md"), report);
            return fail == 0 ? 0 : 1;
        }
    }
}
