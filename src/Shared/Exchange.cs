using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NyangTalk.Shared
{
    /// <summary>
    /// Exchange format between the app and the game component (40-SPEC 4장). Newly defined for NyangTalk.
    /// Folder: %LOCALAPPDATA%\NyangTalk\exchange\ (outside the game folder).
    /// </summary>
    public static class Exchange
    {
        public const string Schema = "1.0.0";
        public const string StatusFile = "status.json";
        public const string ParticipantsFile = "participants.json";
        public const string AppearanceFile = "appearance.json";
        public const string PermitFile = "permit.json";
        public const string SessionKeyFile = "session.key";
        public const string ChestLogFile = "chest-log.ndjson";
        public const string OutboxDir = "outbox";
        public const string ResultsDir = "results";
        public const string PortraitDir = "portraits";
        public const string EmoteDir = "emotes";
        public const int MaxRequestBytes = 8 * 1024;
        public static readonly TimeSpan RequestLifetime = TimeSpan.FromSeconds(30);

        public static string LobbyFile(string lobbyId) { return "lobby-" + SafeId(lobbyId) + ".ndjson"; }

        /// <summary>Lobby IDs are numeric; anything else is reduced to safe file name characters.</summary>
        public static string SafeId(string id)
        {
            var sb = new StringBuilder();
            foreach (var c in id ?? "") if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }

        public static string DefaultFolder()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NyangTalk", "exchange");
        }

        public static string Iso(DateTime utc) { return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture); }

        public static bool TryIso(string s, out DateTime utc)
        {
            return DateTime.TryParseExact(s ?? "", new[] { "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ssZ" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        }

        public static Dictionary<string, object> Header(string producerVersion)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["schema"] = Schema;
            d["producer_version"] = producerVersion;
            return d;
        }

        // ---- send requests: "서명된 세션 값" = HMAC-SHA256(session key, canonical request) ----

        public static string Canonical(string requestId, string lobbyId, string text, string expiresIso)
        {
            return requestId + "\n" + lobbyId + "\n" + expiresIso + "\n" + text;
        }

        public static string Sign(byte[] key, string canonical)
        {
            using (var h = new HMACSHA256(key)) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }

        public static Dictionary<string, object> MakeRequest(byte[] key, string requestId, string lobbyId, string text, DateTime nowUtc)
        {
            var expires = Iso(nowUtc + RequestLifetime);
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            d["schema"] = Schema;
            d["request_id"] = requestId;
            d["lobby_id"] = lobbyId;
            d["text"] = text;
            d["created_utc"] = Iso(nowUtc);
            d["expires_utc"] = expires;
            d["session"] = Sign(key, Canonical(requestId, lobbyId, text, expires));
            return d;
        }

        public enum RequestCheck { Ok, TooLarge, Malformed, BadSession, Expired, WrongSchema }

        /// <summary>Validation done by the game component before anything reaches the game (40-SPEC 3장, 6장 10번).</summary>
        public static RequestCheck Verify(byte[] rawFile, byte[] key, DateTime nowUtc, out Dictionary<string, object> request)
        {
            request = null;
            if (rawFile == null || rawFile.Length > MaxRequestBytes) return RequestCheck.TooLarge;
            object o;
            if (!Json.TryParse(Encoding.UTF8.GetString(rawFile), out o)) return RequestCheck.Malformed;
            var d = Json.Obj(o);
            if (d == null) return RequestCheck.Malformed;
            var schema = Json.Str(d, "schema");
            if (schema == null || !schema.StartsWith("1.", StringComparison.Ordinal)) return RequestCheck.WrongSchema;
            var id = Json.Str(d, "request_id"); var lobby = Json.Str(d, "lobby_id"); var text = Json.Str(d, "text");
            var exp = Json.Str(d, "expires_utc"); var sig = Json.Str(d, "session");
            if (id == null || lobby == null || text == null || exp == null || sig == null) return RequestCheck.Malformed;
            if (key == null || key.Length < 16) return RequestCheck.BadSession;
            var expect = Sign(key, Canonical(id, lobby, text, exp));
            if (!FixedTimeEquals(expect, sig)) return RequestCheck.BadSession;
            DateTime expires;
            if (!TryIso(exp, out expires)) return RequestCheck.Malformed;
            if (nowUtc > expires) return RequestCheck.Expired;
            request = d;
            return RequestCheck.Ok;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static byte[] NewKey()
        {
            var k = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(k);
            return k;
        }
    }
}
