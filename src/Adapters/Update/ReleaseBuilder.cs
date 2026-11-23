using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NyangTalk.Domain.Update;
using NyangTalk.Shared;

namespace NyangTalk.Adapters.Update
{
    /// <summary>
    /// Start-up side of updates: switch to a staged version, wait for its "started" mark, roll back otherwise
    /// (20-SPEC 4장 7–9번). Process start and waiting are injectable for tests.
    /// </summary>
    public sealed class Launcher
    {
        private readonly VersionStore _store;
        public Func<string, string, Process> Start = (exe, args) => Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
        public TimeSpan ConfirmWindow = UpdatePolicy.StartupConfirmWindow;
        public Action<int> Sleep = ms => System.Threading.Thread.Sleep(ms);

        public Launcher(VersionStore store) { _store = store; }
        /// <summary>Extra arguments passed on to the version it starts (e.g. --wait-single after "지금 재시작").</summary>
        public string ForwardArgs = "";

        public enum Outcome { RunSelf, HandedOver, RolledBack }

        public Outcome OnStart(string selfVersion, bool startedByLauncher, out string rolledBack)
        {
            rolledBack = null;
            if (startedByLauncher) return Outcome.RunSelf;
            var staged = _store.ActivateStaged(selfVersion);
            var target = staged ?? _store.Current;
            if (target == null || target == selfVersion || !File.Exists(_store.ExeFor(target))) return Outcome.RunSelf;
            Process p;
            try { p = Start(_store.ExeFor(target), "--from-launcher" + ForwardArgs); }
            catch (System.ComponentModel.Win32Exception) { p = null; }
            catch (InvalidOperationException) { p = null; }
            if (p == null && _store.PendingConfirm != target) return Outcome.RunSelf; // could not start it: keep this one running
            if (_store.PendingConfirm != target) return Outcome.HandedOver; // already confirmed earlier: plain hand-over
            var deadline = DateTime.UtcNow + ConfirmWindow;
            while (DateTime.UtcNow < deadline)
            {
                if (_store.PendingConfirm == null) return Outcome.HandedOver;
                if (p == null || p.HasExited) break;
                Sleep(200);
            }
            if (_store.PendingConfirm == null) return Outcome.HandedOver;
            rolledBack = _store.Rollback();
            try { if (p != null && !p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
            return Outcome.RolledBack;
        }
    }

    /// <summary>Password-protected private key file (20-SPEC 5장: not tied to a PC or Windows account).</summary>
    public static class KeyFile
    {
        public static string Create(ECDsa key, string password)
        {
            var p = key.ExportParameters(true);
            var plain = p.D.Concat(p.Q.X).Concat(p.Q.Y).ToArray();
            var salt = new byte[16]; var iv = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(salt); rng.GetBytes(iv); }
            const int iter = 200000;
            byte[] data;
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256))
            using (var aes = Aes.Create())
            {
                aes.Key = kdf.GetBytes(32); aes.IV = iv;
                using (var enc = aes.CreateEncryptor()) data = enc.TransformFinalBlock(plain, 0, plain.Length);
            }
            Array.Clear(plain, 0, plain.Length);
            return Json.Write(new Dictionary<string, object>
            {
                { "format", "nyangtalk-key-1" }, { "kdf", "pbkdf2-sha256" }, { "iterations", (long)iter }, { "salt", Convert.ToBase64String(salt) },
                { "iv", Convert.ToBase64String(iv) }, { "data", Convert.ToBase64String(data) }, { "public", Convert.ToBase64String(Signatures.PublicOf(key)) }
            });
        }

        public static ECDsa Open(string json, string password)
        {
            var d = Json.Obj(Json.Parse(json));
            var salt = Convert.FromBase64String(Json.Str(d, "salt"));
            var iv = Convert.FromBase64String(Json.Str(d, "iv"));
            var data = Convert.FromBase64String(Json.Str(d, "data"));
            byte[] plain;
            using (var kdf = new Rfc2898DeriveBytes(password, salt, (int)Json.Long(d, "iterations", 200000), HashAlgorithmName.SHA256))
            using (var aes = Aes.Create())
            {
                aes.Key = kdf.GetBytes(32); aes.IV = iv;
                using (var dec = aes.CreateDecryptor()) plain = dec.TransformFinalBlock(data, 0, data.Length);
            }
            var p = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = plain.Take(32).ToArray(), Q = new ECPoint { X = plain.Skip(32).Take(32).ToArray(), Y = plain.Skip(64).Take(32).ToArray() } };
            Array.Clear(plain, 0, plain.Length);
            return ECDsa.Create(p);
        }
    }

    public sealed class ReleaseRequest
    {
        public string Version;
        public string Channel = "stable";
        public string ExePath;
        public long Sequence;
        public string MinFromVersion;
        public bool GamePayloadChanged;
        public string GameComponentVersion = "0.1.0";
        public string GameComponentAccepts = ">=0.1.0 <0.2.0";
        public Dictionary<string, string> Notes = new Dictionary<string, string>();
        public List<string> Revoked = new List<string>();
        public string AssetUrlBase;              // e.g. https://github.com/<owner>/<repo>/releases/download
        public List<string> ExistingTags = new List<string>();
        public string PreviousPublicVersion;
        public bool PublicApiBreaking;
        public string Changelog;
        public bool ChecksPassed;   // must be set explicitly by a real check run
        /// <summary>Folder holding the LGPL-2.1 source archives attached to every release (third_party/sources).</summary>
        public string SourceDir;
    }

    /// <summary>
    /// Release steps (20-SPEC 6장): checks → update.json from the build → sign → verify the signature with the keys the
    /// app embeds → summary → approval → upload. Any failure stops before upload.
    /// </summary>
    public static class ReleaseBuilder
    {
        /// <summary>
        /// LGPL-2.1 components shipped in the connector (BepInEx, UnityDoorstop): their complete source goes with every
        /// release, next to the exe (LGPL-2.1 section 4, last paragraph). Name and SHA-256 as recorded in third_party/SOURCES.md.
        /// </summary>
        public static readonly string[][] LgplSources =
        {
            new[] { "BepInEx-5.4.23.6-src.zip", "ced5fec76c315e54a775c56f3f04a2a0e62b5c3fb16b2d98f00a2a550ba7197e" },
            new[] { "UnityDoorstop-4.6.0-src.zip", "95a3d62f5cb2fc792f1dfc61e6b09f78e685e0966401e6f9e6fa58540249e0ea" },
        };

        /// <summary>Paths of the source archives to attach, or the reason they cannot be attached.</summary>
        public static List<string> SourceArchives(string dir, out string problem)
        {
            problem = null;
            var list = new List<string>();
            foreach (var s in LgplSources)
            {
                var path = dir == null ? null : Path.Combine(dir, s[0]);
                if (path == null || !File.Exists(path)) { problem = "LGPL source archive missing: " + s[0]; return null; }
                string sha;
                using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                if (sha != s[1]) { problem = "LGPL source archive changed: " + s[0]; return null; }
                list.Add(path);
            }
            return list;
        }

        public sealed class Result
        {
            public bool Uploaded;
            public string StoppedAt;
            public string UpdateJson;
            public string Signature;
            public string Summary;
            public List<string> SourceArchives;
        }

        public static Result Run(ReleaseRequest r, ECDsa key, IList<byte[]> embeddedKeys, Func<string, bool> approve, Action<string, string, string> upload)
        {
            var res = new Result();
            SemVersion v;
            if (!SemVersion.TryParse(r.Version, out v)) { res.StoppedAt = "version is not SemVer"; return res; }
            if (!r.ChecksPassed) { res.StoppedAt = "checks failed"; return res; }
            if (r.ExistingTags.Contains("v" + r.Version)) { res.StoppedAt = "version already used"; return res; }
            if (r.Changelog == null || !Regex.IsMatch(r.Changelog, @"^## \[" + Regex.Escape(r.Version) + @"\]", RegexOptions.Multiline)) { res.StoppedAt = "CHANGELOG has no entry"; return res; }
            SemVersion prev;
            if (r.PublicApiBreaking && r.PreviousPublicVersion != null && SemVersion.TryParse(r.PreviousPublicVersion, out prev) && prev.Major >= 1 && v.Major == prev.Major)
            { res.StoppedAt = "public API incompatible but MAJOR unchanged"; return res; }
            if (r.Channel == "stable" && v.IsPrerelease) { res.StoppedAt = "pre-release on stable channel"; return res; }
            Uri baseUri;
            if (r.AssetUrlBase == null || !Uri.TryCreate(r.AssetUrlBase, UriKind.Absolute, out baseUri) || baseUri.Scheme != "https") { res.StoppedAt = "asset URL base must be absolute https"; return res; }
            string sourceProblem;
            res.SourceArchives = SourceArchives(r.SourceDir, out sourceProblem);
            if (sourceProblem != null) { res.StoppedAt = sourceProblem; return res; }
            var exe = File.ReadAllBytes(r.ExePath);
            string sha;
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(exe)).Replace("-", "").ToLowerInvariant();
            var name = "NyangTalk-" + r.Version + ".exe";
            var info = new Dictionary<string, object>
            {
                { "product", UpdatePolicy.Product }, { "channel", r.Channel }, { "version", r.Version },
                { "game_component", new Dictionary<string, object> { { "version", r.GameComponentVersion }, { "accepts", r.GameComponentAccepts } } },
                { "published_utc", Exchange.Iso(DateTime.UtcNow) }, { "sequence", r.Sequence }, { "min_from_version", r.MinFromVersion },
                { "asset", new Dictionary<string, object> { { "name", name }, { "url", (r.AssetUrlBase ?? "") + "/v" + r.Version + "/" + name }, { "size", (long)exe.Length }, { "sha256", sha } } },
                { "game_payload_changed", r.GamePayloadChanged },
                { "notes", r.Notes.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "revoked", r.Revoked.Cast<object>().ToList() },
                { "next_keys", embeddedKeys.Select(k => (object)Signatures.Fingerprint(k)).ToList() }
            };
            res.UpdateJson = Json.Write(info);
            try
            {
                var why = UpdateService.VerifyAsset(r.ExePath, exe, ReleaseInfoParser.Parse(Encoding.UTF8.GetBytes(res.UpdateJson)));
                if (why != null) { res.StoppedAt = "built exe does not match update.json: " + why; return res; }
            }
            catch (FormatException ex) { res.StoppedAt = "update.json invalid: " + ex.Message; return res; }
            var bytes = Encoding.UTF8.GetBytes(res.UpdateJson);
            res.Signature = Signatures.Sign(bytes, key);
            if (!Signatures.Verify(bytes, res.Signature, embeddedKeys)) { res.StoppedAt = "signature round-trip failed"; return res; }
            try { ReleaseInfoParser.Parse(bytes); } catch (FormatException ex) { res.StoppedAt = "update.json invalid: " + ex.Message; return res; }
            res.Summary = "version " + r.Version + " (" + r.Channel + ")\nsha256 " + sha + "\nsize " + exe.Length + "\ngame_payload_changed " + r.GamePayloadChanged + "\nnotes " + string.Join(" | ", r.Notes.Values)
                + "\nLGPL sources attached: " + string.Join(", ", res.SourceArchives.Select(Path.GetFileName));
            if (!approve(res.Summary)) { res.StoppedAt = "not approved"; return res; }
            upload(r.Version, res.UpdateJson, res.Signature);
            res.Uploaded = true;
            return res;
        }
    }
}
