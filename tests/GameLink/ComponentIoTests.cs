using System;
using System.IO;
using System.Text;
using System.Threading;
using NyangTalk.Shared;
using NyangTalk.Adapters.Storage;

namespace NyangTalk.Tests
{
    public static class ComponentIoTests
    {
        [Test("40-4,80-4.2,component-io")]
        public static void PermitReaderAllowsTheAtomicWritersExistingHandle()
        {
            var file = Path.Combine(Ctx.NewDir("permit-sharing"), Exchange.PermitFile);
            var bytes = Encoding.UTF8.GetBytes("{\"enabled\":true}");
            File.WriteAllBytes(file, bytes);
            using (var writer = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                A.Throws<IOException>(() => File.ReadAllBytes(file), "old default sharing reproduces the actual Windows sharing violation");
                A.Eq(Convert.ToBase64String(bytes), Convert.ToBase64String(ExchangeFiles.ReadAllBytes(file)), "shared snapshot reader remains readable");
            }
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                A.Throws<IOException>(() => ExchangeFiles.ReadAllBytes(file), "a real exclusive lock remains a visible error");
        }

        [Test("40-4,80-4.2,component-io")]
        public static void PermitSnapshotsStayCompleteDuringAtomicReplacement()
        {
            var file = Path.Combine(Ctx.NewDir("permit-replacement"), Exchange.PermitFile);
            AtomicFile.WriteAllText(file, "{\"value\":0,\"copy\":0}");
            Exception failed = null;
            using (var start = new ManualResetEvent(false))
            {
                var writer = new Thread(() =>
                {
                    try
                    {
                        start.WaitOne();
                        for (int i = 1; i <= 400; i++) AtomicFile.WriteAllText(file, "{\"value\":" + i + ",\"copy\":" + i + "}");
                    }
                    catch (Exception ex) { failed = ex; }
                });
                writer.Start(); start.Set();
                try
                {
                    for (int i = 0; i < 800; i++)
                    {
                        var snapshot = Json.Obj(Json.Parse(Encoding.UTF8.GetString(ExchangeFiles.ReadAllBytes(file))));
                        A.Eq(Json.Long(snapshot, "value", -1), Json.Long(snapshot, "copy", -2), "read a complete old or new snapshot");
                    }
                }
                finally { A.True(writer.Join(10000), "background writer completed"); }
            }
            if (failed != null) throw failed;
            A.Eq(400L, Json.Long(Json.Obj(Json.Parse(File.ReadAllText(file))), "value", -1), "atomic writer was never prevented from finishing");
        }

        [Test("40-4,component-io")]
        public static void SnapshotReaderRejectsOversizedControlFiles()
        {
            var file = Path.Combine(Ctx.NewDir("permit-size"), Exchange.PermitFile);
            File.WriteAllBytes(file, new byte[ExchangeFiles.MaxControlBytes + 1]);
            A.Throws<InvalidDataException>(() => ExchangeFiles.ReadAllBytes(file), "control file cap is explicit");
        }

        [Test("40-7,80-4.2,component-io")]
        public static void RecoveredOperationClearsOnlyItsOwnCurrentFault()
        {
            var errors = new ComponentErrors(); int reports = 0;
            Action<Exception> report = ex => reports++;
            A.False(errors.Run("chest", () => { throw new IOException("permit locked"); }, report), "failure is reported");
            A.Eq("chest: IOException", errors.Error, "current failure appears in status");
            errors.Run("chest", () => { throw new IOException("permit locked"); }, report);
            A.Eq(1, reports, "identical persistent fault does not flood the game log");
            errors.Run("chest", () => { throw new IOException("different file failure"); }, report);
            A.Eq(2, reports, "different I/O causes with the same exception type are still logged");
            errors.Set("catalog compatibility", "unsupported-version");
            errors.Run("status", () => { }, report);
            A.True(errors.Error.Contains("chest: IOException"), "unrelated successful work cannot clear the chest fault");
            A.True(errors.Run("chest", () => { }, report), "next successful chest tick recovers");
            A.Eq("catalog compatibility: unsupported-version", errors.Error, "persistent startup fault is retained");
            errors.Clear("catalog compatibility");
            A.Eq<string>(null, errors.Error, "fully healthy status has no stale error");
            errors.Run("chest", () => { throw new IOException("locked again"); }, report);
            A.Eq(3, reports, "a new failure after recovery is reported again");
        }
    }
}
