using System;
using System.IO;

namespace NyangTalk.Adapters.Update
{
    /// <summary>
    /// Where updates come from (20-SPEC 2장). The release repository is fixed in the build; there is no address field on
    /// screen. A one-line HTTPS address in <c>update-override.txt</c> in the app data folder replaces it for testing; an
    /// invalid line is ignored and the default address is used.
    /// </summary>
    public sealed class UpdateSource
    {
        public const string Repository = "Alexandros509/NyangTalk";
        public const string DisplayName = "github.com/" + Repository;
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
        public const string DefaultInfoUrl = "https://github.com/" + Repository + "/releases/latest/download/update.json";
        public const string BetaInfoUrl = "https://github.com/" + Repository + "/releases/latest/download/update-beta.json";
        public const string OverrideFile = "update-override.txt";

        public Uri OverrideUrl;          // non-null only when a valid test address is in use
        public string IgnoredOverride;   // the file existed but its line was not a valid HTTPS address

        public bool Overridden { get { return OverrideUrl != null; } }

        /// <summary>Address of the update information for a channel ("stable" or "beta").</summary>
        public Uri InfoUrl(string channel)
        {
            if (OverrideUrl != null) return OverrideUrl;
            return new Uri(channel == "beta" ? BetaInfoUrl : DefaultInfoUrl);
        }

        /// <summary>Reads the override file from the data folder; a missing or unreadable file means the default.</summary>
        public static UpdateSource Resolve(string dataRoot)
        {
            var path = Path.Combine(dataRoot, OverrideFile);
            if (!File.Exists(path)) return new UpdateSource();
            try { return FromOverrideText(File.ReadAllText(path)); }
            catch (IOException ex) { return new UpdateSource { IgnoredOverride = ex.Message }; }
            catch (UnauthorizedAccessException ex) { return new UpdateSource { IgnoredOverride = ex.Message }; }
        }

        /// <summary>The file must hold exactly one line with an absolute HTTPS address; anything else is ignored.</summary>
        public static UpdateSource FromOverrideText(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Trim('﻿', ' ', '\t', '\n', '\r').Split('\n');
            var line = lines.Length == 1 ? lines[0].Trim() : null;
            Uri u;
            if (line != null && Uri.TryCreate(line, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(u.Host) && line.IndexOf(' ') < 0)
                return new UpdateSource { OverrideUrl = u };
            return new UpdateSource { IgnoredOverride = line ?? "(" + lines.Length + " lines)" };
        }
    }
}
