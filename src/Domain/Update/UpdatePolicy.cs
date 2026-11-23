using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Domain.Update
{
    public sealed class UpdateAsset
    {
        public string Name;
        public string Url;
        public long Size;
        public string Sha256;
    }

    /// <summary>update.json contents (20-SPEC 3장), after signature verification.</summary>
    public sealed class ReleaseInfo
    {
        public string Product;
        public string Channel;
        public SemVersion Version;
        public string GameComponentVersion;
        public string GameComponentAccepts;
        public DateTime PublishedUtc;
        public long Sequence;
        public SemVersion MinFromVersion;
        public UpdateAsset Asset;
        public bool GamePayloadChanged;
        public Dictionary<string, string> Notes = new Dictionary<string, string>();
        public List<SemVersion> Revoked = new List<SemVersion>();
        public List<string> NextKeys = new List<string>();
    }

    public enum UpdateDecision
    {
        NewVersion,
        NotNewer,
        WrongProduct,
        WrongChannel,
        PrereleaseOnStable,
        SequenceRollback,
        ManualOnly,          // current < min_from_version
        BlockedAfterRollback // same version failed to start before
    }

    public static class UpdatePolicy
    {
        public const string Product = "nyangtalk";
        public const long MaxInfoBytes = 64 * 1024;
        public const long MaxAssetBytes = 200L * 1024 * 1024;
        public const int MaxRedirects = 5;
        public const int NotesMaxChars = 500;
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
        public static readonly TimeSpan StartupConfirmWindow = TimeSpan.FromSeconds(20);

        public static UpdateDecision Decide(ReleaseInfo info, SemVersion current, string userChannel, long highestSeenSequence, SemVersion blockedVersion)
        {
            if (info.Product != Product) return UpdateDecision.WrongProduct;
            if (info.Channel != userChannel) return UpdateDecision.WrongChannel;
            if (userChannel == "stable" && info.Version.IsPrerelease) return UpdateDecision.PrereleaseOnStable;
            if (info.Sequence < highestSeenSequence) return UpdateDecision.SequenceRollback;
            if (!(info.Version > current)) return UpdateDecision.NotNewer;
            if (blockedVersion != null && info.Version == blockedVersion) return UpdateDecision.BlockedAfterRollback;
            if (info.MinFromVersion != null && current < info.MinFromVersion) return UpdateDecision.ManualOnly;
            return UpdateDecision.NewVersion;
        }

        public static bool IsRevoked(ReleaseInfo info, SemVersion current)
        {
            return info.Revoked.Any(v => v == current);
        }

        /// <summary>Redirects must stay on HTTPS and on allowed hosts, at most 5 hops (20-SPEC 4장 2번).</summary>
        public static bool RedirectAllowed(Uri target, int hop, ICollection<string> allowedHosts)
        {
            if (hop > MaxRedirects) return false;
            if (target == null || target.Scheme != "https") return false;
            return allowedHosts.Any(h => string.Equals(h, target.Host, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Restart now is deferred while a draft exists, a send is pending, or the game files are being installed.</summary>
        public static bool CanRestartNow(bool hasDraft, bool sendPending, bool installing)
        {
            return !hasDraft && !sendPending && !installing;
        }
    }
}
