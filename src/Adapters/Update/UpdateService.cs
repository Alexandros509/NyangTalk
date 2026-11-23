using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NyangTalk.Adapters.Storage;
using NyangTalk.Application;
using NyangTalk.Domain.Update;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.Update
{
    public sealed class HttpResult
    {
        public int Status;
        public Uri Location;
        public byte[] Body;
        public bool TooLarge;
    }

    public interface IHttp { HttpResult Get(Uri url, long maxBytes, string userAgent); }

    /// <summary>HTTPS GET without automatic redirects, cookies or credentials (20-SPEC 4장 2번).</summary>
    public sealed class RealHttp : IHttp
    {
        public HttpResult Get(Uri url, long maxBytes, string userAgent)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = false;
            req.UseDefaultCredentials = false;
            req.CookieContainer = null;
            req.UserAgent = userAgent;
            req.Timeout = 30000;
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex) { resp = ex.Response as HttpWebResponse; if (resp == null) throw; }
            using (resp)
            {
                var r = new HttpResult { Status = (int)resp.StatusCode };
                var loc = resp.Headers["Location"];
                if (loc != null) r.Location = new Uri(url, loc);
                if (resp.ContentLength > maxBytes) { r.TooLarge = true; return r; }
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > maxBytes) { r.TooLarge = true; return r; }
                    }
                    r.Body = ms.ToArray();
                }
                return r;
            }
        }
    }

    public enum UpdateStatus { NotConfigured, CheckFailed, BadSignature, BadInfo, NoUpdate, UpdateAvailable, ManualOnly, Downloaded, BadDownload, RedirectRefused, TooLarge, Refused, StateNotSaved }

    public sealed class UpdateCheck
    {
        public UpdateStatus Status;
        public ReleaseInfo Info;
        public UpdateDecision Decision;
        public string Detail;
        public bool Revoked;
    }

    /// <summary>ECDSA P-256 / SHA-256 signatures; signature bytes are r‖s (64 bytes) in Base64 (docs/decisions.md D7).</summary>
    public static class Signatures
    {
        public static ECDsa FromPublic(byte[] xy)
        {
            var p = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = xy.Take(32).ToArray(), Y = xy.Skip(32).Take(32).ToArray() } };
            return ECDsa.Create(p);
        }

        public static bool Verify(byte[] data, string signatureBase64, IEnumerable<byte[]> publicKeys)
        {
            byte[] sig;
            try { sig = Convert.FromBase64String((signatureBase64 ?? "").Trim()); } catch (FormatException) { return false; }
            if (sig.Length != 64) return false;
            foreach (var k in publicKeys)
            {
                if (k == null || k.Length != 64) continue;
                using (var e = FromPublic(k)) if (e.VerifyData(data, sig, HashAlgorithmName.SHA256)) return true;
            }
            return false;
        }

        public static string Sign(byte[] data, ECDsa key) { return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256)); }

        public static byte[] PublicOf(ECDsa key)
        {
            var p = key.ExportParameters(false);
            return p.Q.X.Concat(p.Q.Y).ToArray();
        }

        public static string Fingerprint(byte[] publicKey)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(publicKey)).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class ReleaseInfoParser
    {
        /// <summary>Parses update.json after its signature was verified. Any invalid field rejects the whole info.</summary>
        public static ReleaseInfo Parse(byte[] bytes)
        {
            var d = Json.Obj(Json.Parse(Encoding.UTF8.GetString(bytes)));
            if (d == null) throw new FormatException("not an object");
            var info = new ReleaseInfo { Product = Json.Str(d, "product"), Channel = Json.Str(d, "channel"), Sequence = Json.Long(d, "sequence", -1) };
            SemVersion v;
            if (!SemVersion.TryParse(Json.Str(d, "version"), out v)) throw new FormatException("version is not SemVer");
            info.Version = v;
            var min = Json.Str(d, "min_from_version");
            if (min != null) { if (!SemVersion.TryParse(min, out v)) throw new FormatException("min_from_version"); info.MinFromVersion = v; }
            DateTime pub;
            if (Exchange.TryIso(Json.Str(d, "published_utc"), out pub)) info.PublishedUtc = pub;
            var gc = Json.Obj(d.ContainsKey("game_component") ? d["game_component"] : null);
            if (gc != null) { info.GameComponentVersion = Json.Str(gc, "version"); info.GameComponentAccepts = Json.Str(gc, "accepts"); }
            var a = Json.Obj(d.ContainsKey("asset") ? d["asset"] : null);
            if (a == null) throw new FormatException("asset");
            info.Asset = new UpdateAsset { Name = Json.Str(a, "name"), Url = Json.Str(a, "url"), Size = Json.Long(a, "size", -1), Sha256 = (Json.Str(a, "sha256") ?? "").ToLowerInvariant() };
            Uri assetUri;
            if (info.Asset.Size <= 0 || info.Asset.Sha256.Length != 64 || info.Asset.Url == null || !Uri.TryCreate(info.Asset.Url, UriKind.Absolute, out assetUri) || assetUri.Scheme != "https")
                throw new FormatException("asset fields");
            info.GamePayloadChanged = Json.Bool(d, "game_payload_changed", false);
            var notes = Json.Obj(d.ContainsKey("notes") ? d["notes"] : null);
            if (notes != null) foreach (var kv in notes) { var s = kv.Value as string; if (s != null) { if (s.Length > UpdatePolicy.NotesMaxChars) throw new FormatException("notes too long"); info.Notes[kv.Key] = s; } }
            foreach (var o in Json.List(d, "revoked") ?? new List<object>()) { SemVersion r; if (o is string && SemVersion.TryParse((string)o, out r)) info.Revoked.Add(r); }
            foreach (var o in Json.List(d, "next_keys") ?? new List<object>()) if (o is string) info.NextKeys.Add((string)o);
            if (info.Sequence < 0) throw new FormatException("sequence");
            return info;
        }
    }

    /// <summary>Update check, download and staging (20-SPEC 4장). Nothing is read from update.json before its signature is verified.</summary>
    public sealed class UpdateService
    {
        public const string StateDoc = "update-state";
        private readonly IHttp _http;
        private readonly IDocumentStore _docs;
        private readonly SemVersion _current;
        private readonly List<byte[]> _keys;
        private readonly string _stageRoot;
        public Uri InfoUrl;
        public string Channel = "stable";
        public string[] AllowedHosts = { "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };
        public DateTime? LastFailureUtc;
        private bool _stateSaved = true;

        public UpdateService(IHttp http, IDocumentStore docs, SemVersion current, IEnumerable<byte[]> publicKeys, string stageRoot)
        {
            _http = http; _docs = docs; _current = current; _keys = publicKeys.ToList(); _stageRoot = stageRoot;
        }

        public string UserAgent { get { return "NyangTalk/" + _current; } }

        private Dictionary<string, object> State()
        {
            string broken;
            var d = _docs.Load(StateDoc, out broken);
            return d == null ? new Dictionary<string, object>() : new Dictionary<string, object>(d);
        }

        public long HighestSequence { get { return Json.Long(State(), "highest_sequence", 0); } }
        public SemVersion BlockedVersion
        {
            get { SemVersion v; return SemVersion.TryParse(Json.Str(State(), "blocked_version"), out v) ? v : null; }
        }

        public void Block(SemVersion v) { var s = State(); s["blocked_version"] = v.ToString(); _docs.Save(StateDoc, s); }

        /// <summary>Follows redirects only over HTTPS to allowed hosts, at most 5 hops.</summary>
        public HttpResult Fetch(Uri url, long max, out UpdateStatus failure)
        {
            failure = UpdateStatus.CheckFailed;
            if (url.Scheme != "https") { failure = UpdateStatus.RedirectRefused; return null; }
            for (int hop = 0; ; hop++)
            {
                var r = _http.Get(url, max, UserAgent);
                if (r.TooLarge) { failure = UpdateStatus.TooLarge; return null; }
                if (r.Status >= 300 && r.Status < 400 && r.Location != null)
                {
                    if (!UpdatePolicy.RedirectAllowed(r.Location, hop + 1, AllowedHosts)) { failure = UpdateStatus.RedirectRefused; return null; }
                    url = r.Location;
                    continue;
                }
                if (r.Status != 200 || r.Body == null) return null;
                return r;
            }
        }

        public UpdateCheck Check(DateTime nowUtc)
        {
            if (InfoUrl == null || _keys.Count == 0) return new UpdateCheck { Status = UpdateStatus.NotConfigured };
            UpdateStatus fail;
            HttpResult info, sig;
            try
            {
                info = Fetch(InfoUrl, UpdatePolicy.MaxInfoBytes, out fail);
                if (info == null) { LastFailureUtc = nowUtc; return new UpdateCheck { Status = fail }; }
                sig = Fetch(new Uri(InfoUrl + ".sig"), 4096, out fail);
                if (sig == null) { LastFailureUtc = nowUtc; return new UpdateCheck { Status = fail }; }
            }
            catch (WebException ex) { LastFailureUtc = nowUtc; return new UpdateCheck { Status = UpdateStatus.CheckFailed, Detail = ex.Status.ToString() }; }
            catch (IOException ex) { LastFailureUtc = nowUtc; return new UpdateCheck { Status = UpdateStatus.CheckFailed, Detail = ex.Message }; }
            // signature first: the bytes are not interpreted before this passes
            if (!Signatures.Verify(info.Body, Encoding.ASCII.GetString(sig.Body), _keys)) return new UpdateCheck { Status = UpdateStatus.BadSignature };
            ReleaseInfo ri;
            try { ri = ReleaseInfoParser.Parse(info.Body); }
            catch (FormatException ex) { return new UpdateCheck { Status = UpdateStatus.BadInfo, Detail = ex.Message }; }
            var decision = UpdatePolicy.Decide(ri, _current, Channel, HighestSequence, BlockedVersion);
            var result = new UpdateCheck { Info = ri, Decision = decision, Revoked = UpdatePolicy.IsRevoked(ri, _current) };
            _stateSaved = true;
            if (decision != UpdateDecision.SequenceRollback && decision != UpdateDecision.WrongProduct && decision != UpdateDecision.WrongChannel && ri.Sequence > HighestSequence)
            {
                var s = State(); s["highest_sequence"] = ri.Sequence; _stateSaved = _docs.Save(StateDoc, s);
            }
            switch (decision)
            {
                case UpdateDecision.NewVersion: result.Status = UpdateStatus.UpdateAvailable; break;
                case UpdateDecision.ManualOnly: result.Status = UpdateStatus.ManualOnly; break;
                case UpdateDecision.NotNewer: result.Status = UpdateStatus.NoUpdate; break;
                default:   // SequenceRollback, WrongProduct, WrongChannel, PrereleaseOnStable, BlockedAfterRollback
                    result.Status = UpdateStatus.Refused; result.Detail = decision.ToString(); break;   // signed but not acceptable: never "최신"
            }
            if (!_stateSaved) { result.Status = UpdateStatus.StateNotSaved; result.Detail = "highest_sequence"; }
            return result;
        }

        /// <summary>Downloads to a temp folder, verifies size, hash, PE format and product/version, then stages it.</summary>
        public UpdateCheck Download(UpdateCheck check)
        {
            var info = check.Info;
            UpdateStatus fail;
            if (info.Asset.Size > UpdatePolicy.MaxAssetBytes) return new UpdateCheck { Status = UpdateStatus.TooLarge, Info = info };
            HttpResult r;
            try { r = Fetch(new Uri(info.Asset.Url), Math.Min(UpdatePolicy.MaxAssetBytes, info.Asset.Size), out fail); }
            catch (WebException) { return new UpdateCheck { Status = UpdateStatus.CheckFailed, Info = info }; }
            catch (IOException) { return new UpdateCheck { Status = UpdateStatus.CheckFailed, Info = info }; }
            if (r == null) return new UpdateCheck { Status = fail, Info = info };
            var tmpDir = Path.Combine(_stageRoot, "download-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);
            var tmp = Path.Combine(tmpDir, "NyangTalk.exe");
            File.WriteAllBytes(tmp, r.Body);
            string why = VerifyAsset(tmp, r.Body, info);
            if (why != null) { Directory.Delete(tmpDir, true); return new UpdateCheck { Status = UpdateStatus.BadDownload, Info = info, Detail = why }; }
            var versions = new VersionStore(_stageRoot);
            versions.Stage(info.Version.ToString(), tmp, info.GamePayloadChanged);
            Directory.Delete(tmpDir, true);
            return new UpdateCheck { Status = UpdateStatus.Downloaded, Info = info };
        }

        public static string VerifyAsset(string path, byte[] body, ReleaseInfo info)
        {
            if (body.Length != info.Asset.Size) return "size";
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(body)).Replace("-", "").ToLowerInvariant();
            if (hash != info.Asset.Sha256) return "sha256";
            if (body.Length < 2 || body[0] != 'M' || body[1] != 'Z') return "not an executable";
            var vi = FileVersionInfo.GetVersionInfo(path);
            if (vi.ProductName != "NyangTalk") return "product name";
            if (vi.ProductVersion != info.Version.ToString()) return "version " + vi.ProductVersion;
            return null;
        }
    }

    /// <summary>
    /// Version folders with a "current" pointer (20-SPEC 4장 8–9번). The previous version stays until the next
    /// successful update; a new version that does not confirm its start within 20 s is rolled back.
    /// </summary>
    public sealed class VersionStore
    {
        private readonly string _root;
        public VersionStore(string root) { _root = root; Directory.CreateDirectory(Path.Combine(root, "versions")); }
        private string StatePath { get { return Path.Combine(_root, "versions", "current.json"); } }

        /// <summary>Set when current.json was damaged; the damaged file is kept as .broken-&lt;date&gt;.</summary>
        public string BrokenCopy { get; private set; }

        public Dictionary<string, object> Load()
        {
            var t = AtomicFile.ReadAllText(StatePath);
            object o;
            if (t == null) return new Dictionary<string, object>();
            if (Json.TryParse(t, out o) && o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            var copy = StatePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(StatePath, copy); BrokenCopy = copy; } catch (IOException) { BrokenCopy = StatePath; }
            return new Dictionary<string, object>();
        }

        private void Save(Dictionary<string, object> d) { AtomicFile.WriteAllText(StatePath, Json.Write(d)); }

        public string ExeFor(string version) { return Path.Combine(_root, "versions", version, "NyangTalk.exe"); }

        public void Stage(string version, string file, bool gamePayloadChanged)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ExeFor(version)));
            File.Copy(file, ExeFor(version), true);
            var d = Load();
            d["staged"] = version;
            d["staged_game_payload_changed"] = gamePayloadChanged;
            Save(d);
        }

        /// <summary>At start ("다음에 앱을 켤 때"): a staged version becomes current, the old one becomes previous.</summary>
        public string ActivateStaged(string runningVersion)
        {
            var d = Load();
            var staged = Json.Str(d, "staged");
            if (staged == null || !File.Exists(ExeFor(staged))) return null;
            d["previous"] = Json.Str(d, "current") ?? runningVersion;
            d["current"] = staged;
            d["pending_confirm"] = staged;
            d["game_payload_changed"] = Json.Bool(d, "staged_game_payload_changed", false);
            d.Remove("staged"); d.Remove("staged_game_payload_changed");
            Save(d);
            return staged;
        }

        public string Current { get { return Json.Str(Load(), "current"); } }
        public string Previous { get { return Json.Str(Load(), "previous"); } }
        public string PendingConfirm { get { return Json.Str(Load(), "pending_confirm"); } }

        /// <summary>The new version calls this once its window is up ("정상 시작" 표시).</summary>
        public void ConfirmStarted(string version)
        {
            var d = Load();
            if (Json.Str(d, "pending_confirm") != version) return;
            d.Remove("pending_confirm");
            Save(d);
        }

        /// <summary>Returns true if the first run of this version should show the "restart the game" notice, once.</summary>
        public bool TakeGamePayloadNotice(string version)
        {
            var d = Load();
            if (Json.Str(d, "current") != version || !Json.Bool(d, "game_payload_changed", false)) return false;
            d["game_payload_changed"] = false;
            Save(d);
            return true;
        }

        /// <summary>No confirmation in time: go back to the previous version and remember not to retry this one.</summary>
        public string Rollback()
        {
            var d = Load();
            var failed = Json.Str(d, "pending_confirm");
            if (failed == null) return null;
            d["current"] = Json.Str(d, "previous");
            d["blocked"] = failed;
            d.Remove("pending_confirm");
            Save(d);
            return failed;
        }

        public string Blocked { get { return Json.Str(Load(), "blocked"); } }

        /// <summary>After a confirmed update, older version folders except previous are removed.</summary>
        public void CleanupOld()
        {
            var keep = new HashSet<string>(new[] { Current, Previous }.Where(x => x != null));
            foreach (var dir in Directory.GetDirectories(Path.Combine(_root, "versions")))
                if (!keep.Contains(Path.GetFileName(dir))) { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }
}
