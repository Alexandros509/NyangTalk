using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NyangTalk.Adapters.Storage;
using NyangTalk.Adapters.Update;
using NyangTalk.Domain.Update;

namespace NyangTalk.Tests
{
    public sealed class FakeHttp : IHttp
    {
        public readonly Dictionary<string, HttpResult> Map = new Dictionary<string, HttpResult>();
        public readonly List<string> Requests = new List<string>();
        public string LastUserAgent;
        public HttpResult Get(Uri url, long maxBytes, string userAgent)
        {
            Requests.Add(url.ToString());
            LastUserAgent = userAgent;
            HttpResult r;
            if (!Map.TryGetValue(url.ToString(), out r)) return new HttpResult { Status = 404 };
            if (r.Body != null && r.Body.Length > maxBytes) return new HttpResult { Status = 200, TooLarge = true };
            return r;
        }
        public void Put(string url, byte[] body) { Map[url] = new HttpResult { Status = 200, Body = body }; }
    }

    public static class UpdateTests
    {
        private const string InfoUrl = "https://github.com/owner/repo/releases/latest/download/update.json";
        private const string AssetBase = "https://github.com/owner/repo/releases/download";

        public static string FakeExe(string version, string product = "NyangTalk")
        {
            var dir = Ctx.NewDir("exe-" + version);
            var path = Path.Combine(dir, "NyangTalk.exe");
            using (var p = new Microsoft.CSharp.CSharpCodeProvider())
            {
                var src = "[assembly: System.Reflection.AssemblyProduct(\"" + product + "\")][assembly: System.Reflection.AssemblyInformationalVersion(\"" + version + "\")]" +
                          "public static class P { public static void Main() { } }";
                var res = p.CompileAssemblyFromSource(new System.CodeDom.Compiler.CompilerParameters { OutputAssembly = path, GenerateExecutable = true }, src);
                if (res.Errors.HasErrors) throw new Exception(res.Errors[0].ErrorText);
            }
            return path;
        }

        private sealed class Setup
        {
            public FakeHttp Http = new FakeHttp();
            public ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            public ECDsa NextKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            public UpdateService Svc;
            public string Root;
            public string Published;
        }

        private static Setup Make(string current = "0.1.0")
        {
            var s = new Setup { Root = Ctx.NewDir("upd") };
            var keys = new List<byte[]> { Signatures.PublicOf(s.Key), Signatures.PublicOf(s.NextKey) };
            s.Svc = new UpdateService(s.Http, new JsonDocumentStore(Path.Combine(s.Root, "docs")), SemVersion.Parse(current), keys, s.Root) { InfoUrl = new Uri(InfoUrl) };
            return s;
        }

        private static void Publish(Setup s, string version, long seq, ECDsa signer = null, string channel = "stable", Action<byte[]> tamperInfo = null, Action<byte[]> tamperExe = null)
        {
            var exe = FakeExe(version);
            var r = new ReleaseRequest { ChecksPassed = true, Version = version, Channel = channel, ExePath = exe, Sequence = seq, AssetUrlBase = AssetBase, Changelog = "## [" + version + "] - x\n", Notes = { { "ko", "변경" }, { "en", "change" }, { "ja", "変更" } }, SourceDir = Path.Combine(Ctx.Root, "third_party", "sources") };
            var res = ReleaseBuilder.Run(r, signer ?? s.Key, new List<byte[]> { Signatures.PublicOf(s.Key), Signatures.PublicOf(s.NextKey) }, sum => true, (v, j, sig) => { });
            A.True(res.Uploaded, "release built: " + res.StoppedAt);
            var info = Encoding.UTF8.GetBytes(res.UpdateJson);
            if (tamperInfo != null) tamperInfo(info);
            s.Http.Put(InfoUrl, info);
            s.Http.Put(InfoUrl + ".sig", Encoding.ASCII.GetBytes(res.Signature));
            var bytes = File.ReadAllBytes(exe);
            if (tamperExe != null) tamperExe(bytes);
            s.Http.Put(AssetBase + "/v" + version + "/NyangTalk-" + version + ".exe", bytes);
            s.Published = version;
        }

        [Test("20-9.1,20-4.2")]
        public static void NormalUpdateFlow()
        {
            var s = Make();
            Publish(s, "0.2.0", 5);
            var c = s.Svc.Check(Fake.T0);
            A.Eq(UpdateStatus.UpdateAvailable, c.Status, "available: " + c.Detail);
            A.Eq("NyangTalk/0.1.0", s.Http.LastUserAgent, "only product/version in the request");
            var d = s.Svc.Download(c);
            A.Eq(UpdateStatus.Downloaded, d.Status, "downloaded: " + d.Detail);
            var store = new VersionStore(s.Root);
            var launcher = new Launcher(store) { Start = (exe, a) => { store.ConfirmStarted("0.2.0"); return Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true }); }, ConfirmWindow = TimeSpan.FromSeconds(2) };
            string rb;
            A.Eq(Launcher.Outcome.HandedOver, launcher.OnStart("0.1.0", false, out rb), "applied on next start, new version confirmed");
            A.Eq("0.2.0", store.Current, "current switched");
            A.Eq("0.1.0", store.Previous, "previous kept");
            A.True(File.Exists(store.ExeFor("0.2.0")), "version folder");
        }

        [Test("20-9.2")]
        public static void OneByteTamperedInfoRejected()
        {
            var s = Make();
            Publish(s, "0.2.0", 5, tamperInfo: b => b[10] ^= 1);
            A.Eq(UpdateStatus.BadSignature, s.Svc.Check(Fake.T0).Status, "signature fails, nothing downloaded");
            A.False(s.Http.Requests.Any(u => u.EndsWith(".exe")), "no download");
        }

        [Test("20-9.3")]
        public static void TamperedExeDeleted()
        {
            var s = Make();
            Publish(s, "0.2.0", 5, tamperExe: b => b[b.Length - 1] ^= 1);
            var c = s.Svc.Check(Fake.T0);
            var d = s.Svc.Download(c);
            A.Eq(UpdateStatus.BadDownload, d.Status, "hash mismatch");
            A.Eq("sha256", d.Detail, "reason");
            A.False(Directory.GetDirectories(s.Root, "download-*").Any(), "temp removed");
            A.True(new VersionStore(s.Root).ActivateStaged("0.1.0") == null, "nothing staged");
        }

        [Test("20-9.4")]
        public static void LowerSequenceIgnoredAndRemembered()
        {
            var s = Make();
            Publish(s, "0.3.0", 9);
            s.Svc.Check(Fake.T0);
            A.Eq(9L, s.Svc.HighestSequence, "highest stored");
            Publish(s, "0.4.0", 8);
            var c = s.Svc.Check(Fake.T0);
            A.Eq(UpdateDecision.SequenceRollback, c.Decision, "older sequence ignored");
            A.Eq(UpdateStatus.Refused, c.Status, "refused, not reported as latest");
        }

        [Test("20-9.6")]
        public static void RedirectRules()
        {
            var s = Make();
            Publish(s, "0.2.0", 5);
            var body = s.Http.Map[InfoUrl].Body;
            s.Http.Map[InfoUrl] = new HttpResult { Status = 302, Location = new Uri("http://objects.githubusercontent.com/x") };
            A.Eq(UpdateStatus.RedirectRefused, s.Svc.Check(Fake.T0).Status, "http redirect refused");
            s.Http.Map[InfoUrl] = new HttpResult { Status = 302, Location = new Uri("https://evil.example/x") };
            A.Eq(UpdateStatus.RedirectRefused, s.Svc.Check(Fake.T0).Status, "other host refused");
            s.Http.Map[InfoUrl] = new HttpResult { Status = 302, Location = new Uri("https://objects.githubusercontent.com/info") };
            s.Http.Put("https://objects.githubusercontent.com/info", body);
            A.Eq(UpdateStatus.UpdateAvailable, s.Svc.Check(Fake.T0).Status, "allowed redirect followed");
        }

        [Test("20-9.7")]
        public static void SizeCaps()
        {
            var s = Make();
            s.Http.Put(InfoUrl, new byte[UpdatePolicy.MaxInfoBytes + 1]);
            A.Eq(UpdateStatus.TooLarge, s.Svc.Check(Fake.T0).Status, "info > 64KB");
            Publish(s, "0.2.0", 5);
            var c = s.Svc.Check(Fake.T0);
            c.Info.Asset.Size = UpdatePolicy.MaxAssetBytes + 1;
            A.Eq(UpdateStatus.TooLarge, s.Svc.Download(c).Status, "exe > 200MB refused before download");
        }

        [Test("20-9.8")]
        public static void FailedStartRollsBackAndIsNotRetried()
        {
            var s = Make();
            Publish(s, "0.2.0", 5);
            s.Svc.Download(s.Svc.Check(Fake.T0));
            var store = new VersionStore(s.Root);
            // the new version exits immediately without confirming
            var exitedAt = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 1") { UseShellExecute = false, CreateNoWindow = true });
            exitedAt.WaitForExit();
            var launcher = new Launcher(store) { Start = (exe, a) => exitedAt, ConfirmWindow = TimeSpan.FromSeconds(3), Sleep = ms => { } };
            string rb;
            A.Eq(Launcher.Outcome.RolledBack, launcher.OnStart("0.1.0", false, out rb), "rolled back");
            A.Eq("0.2.0", rb, "which version");
            A.Eq("0.1.0", store.Current, "previous is current again");
            s.Svc.Block(SemVersion.Parse(rb));
            var blocked = s.Svc.Check(Fake.T0);
            A.Eq(UpdateDecision.BlockedAfterRollback, blocked.Decision, "same version not fetched again");
            A.Eq(UpdateStatus.Refused, blocked.Status, "shown as refused, never as up to date");
        }

        [Test("20-9.9")]
        public static void NextKeyAccepted()
        {
            var s = Make();
            Publish(s, "0.2.0", 5, signer: s.NextKey);
            A.Eq(UpdateStatus.UpdateAvailable, s.Svc.Check(Fake.T0).Status, "signed with the next key");
            var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var s2 = Make();
            var exe = FakeExe("0.2.0");
            var res = ReleaseBuilder.Run(new ReleaseRequest { ChecksPassed = true, AssetUrlBase = AssetBase, Version = "0.2.0", ExePath = exe, Sequence = 1, Changelog = "## [0.2.0]", SourceDir = Path.Combine(Ctx.Root, "third_party", "sources") }, other, new List<byte[]> { Signatures.PublicOf(other) }, x => true, (a, b, c) => { });
            s2.Http.Put(InfoUrl, Encoding.UTF8.GetBytes(res.UpdateJson)); s2.Http.Put(InfoUrl + ".sig", Encoding.ASCII.GetBytes(res.Signature));
            A.Eq(UpdateStatus.BadSignature, s2.Svc.Check(Fake.T0).Status, "unknown key rejected");
        }

        [Test("20-4.9")]
        public static void LauncherKeepsRunningWhenTargetCannotStart()
        {
            var root = Ctx.NewDir("nostart");
            var store = new VersionStore(root);
            store.Stage("0.2.0", FakeExe("0.2.0"), false);
            store.ActivateStaged("0.1.0");
            store.ConfirmStarted("0.2.0");
            var launcher = new Launcher(store) { Start = (exe, a) => { throw new System.ComponentModel.Win32Exception(5); } };
            string rb;
            A.Eq(Launcher.Outcome.RunSelf, launcher.OnStart("0.1.0", false, out rb), "does not exit with nothing running");
        }

        [Test("20-4.9")]
        public static void DamagedVersionStateKeptAndReported()
        {
            var root = Ctx.NewDir("vbroken");
            var store = new VersionStore(root);
            File.WriteAllText(Path.Combine(root, "versions", "current.json"), "{ not json");
            A.True(store.Current == null, "starts as empty state");
            A.True(store.BrokenCopy != null && File.Exists(store.BrokenCopy), "damaged file kept as .broken copy");
        }

        [Test("20-9.11")]
        public static void GamePayloadNoticeOnFirstRun()
        {
            var root = Ctx.NewDir("payloadnotice");
            var store = new VersionStore(root);
            var exe = FakeExe("0.2.0");
            store.Stage("0.2.0", exe, true);
            store.ActivateStaged("0.1.0");
            A.True(store.TakeGamePayloadNotice("0.2.0"), "first run shows the game restart notice");
            A.False(store.TakeGamePayloadNotice("0.2.0"), "only once");
        }

        [Test("20-9.12,20-9.15")]
        public static void ReleaseStopsBeforeUpload()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keys = new List<byte[]> { Signatures.PublicOf(key) };
            var exe = FakeExe("1.3.0");
            int uploads = 0;
            Action<string, string, string> up = (a, b, c) => uploads++;
            var sources = Path.Combine(Ctx.Root, "third_party", "sources");
            Func<ReleaseRequest> req = () => new ReleaseRequest { ChecksPassed = true, AssetUrlBase = AssetBase, Version = "1.3.0", ExePath = exe, Sequence = 3, Changelog = "# Changelog\n## [1.3.0] - 2026-11-19\n", SourceDir = sources };
            var r = req(); r.ChecksPassed = false;
            A.Eq("checks failed", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "checks");
            A.Eq("signature round-trip failed", ReleaseBuilder.Run(req(), key, new List<byte[]> { Signatures.PublicOf(ECDsa.Create(ECCurve.NamedCurves.nistP256)) }, x => true, up).StoppedAt, "round trip");
            A.Eq("not approved", ReleaseBuilder.Run(req(), key, keys, x => false, up).StoppedAt, "approval refused");
            r = req(); r.ExistingTags.Add("v1.3.0");
            A.Eq("version already used", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "reused number");
            r = req(); r.Changelog = "## [1.2.0]";
            A.Eq("CHANGELOG has no entry", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "changelog");
            r = req(); r.PublicApiBreaking = true; r.PreviousPublicVersion = "1.2.5";
            A.Eq("public API incompatible but MAJOR unchanged", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "breaking without MAJOR");
            r = req(); r.AssetUrlBase = null;
            A.Eq("asset URL base must be absolute https", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "relative asset URL");
            r = req(); r.Version = "1.3.1"; r.Changelog = "## [1.3.1]";
            A.True(ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt.StartsWith("built exe does not match"), "exe version differs from release version");
            r = req(); r.SourceDir = Ctx.NewDir("no-sources");
            A.Eq("LGPL source archive missing: BepInEx-5.4.23.6-src.zip", ReleaseBuilder.Run(r, key, keys, x => true, up).StoppedAt, "no release without the LGPL sources");
            A.Eq(0, uploads, "nothing uploaded in any failing case");
            var ok = ReleaseBuilder.Run(req(), key, keys, x => true, up);
            A.True(ok.Uploaded, "happy path uploads");
            A.Eq(2, ok.SourceArchives.Count, "BepInEx and UnityDoorstop sources attached");
            A.True(ok.Summary.Contains("BepInEx-5.4.23.6-src.zip") && ok.Summary.Contains("UnityDoorstop-4.6.0-src.zip"), "summary lists the sources");
            A.Eq(1, uploads, "once");
        }

        [Test("20-5")]
        public static void KeyFileNeedsPassword()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var file = KeyFile.Create(key, "correct horse");
            A.False(file.Contains(Convert.ToBase64String(key.ExportParameters(true).D)), "private scalar not stored in clear");
            using (var back = KeyFile.Open(file, "correct horse"))
                A.True(Signatures.Verify(new byte[] { 1, 2, 3 }, Signatures.Sign(new byte[] { 1, 2, 3 }, back), new[] { Signatures.PublicOf(key) }), "round trip");
            A.Throws<CryptographicException>(() => KeyFile.Open(file, "wrong"), "wrong password");
        }

        [Test("20-9.14")]
        public static void NonSemVerInfoRejectedWhole()
        {
            foreach (var bad in new[] { "1.4", "v1.4.0", "1.4.0.0" })
            {
                var json = "{\"product\":\"nyangtalk\",\"channel\":\"stable\",\"version\":\"" + bad + "\",\"sequence\":1,\"asset\":{\"name\":\"a\",\"url\":\"https://x\",\"size\":1,\"sha256\":\"" + new string('0', 64) + "\"}}";
                A.Throws<FormatException>(() => ReleaseInfoParser.Parse(Encoding.UTF8.GetBytes(json)), "reject " + bad);
            }
        }
    }
}
