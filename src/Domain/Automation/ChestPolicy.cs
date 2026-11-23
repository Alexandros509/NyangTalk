using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Domain.Automation
{
    public enum ChestStatus { Off, Waiting, Opening, Paused, Unsupported }

    /// <summary>Consent is per device and per feature version (80-SPEC 0장).</summary>
    public sealed class ConsentRecord
    {
        public string DeviceId;
        public int FeatureVersion;
        public DateTime AcceptedUtc;
    }

    public sealed class ChestLogEntry
    {
        public DateTime TimeUtc;
        public string ChestType;
        public bool Success;
        public string Reason;
    }

    /// <summary>
    /// Decides whether the game component may open a chest. Off by default; needs consent; only selected types; waits
    /// a delay after the chest becomes ready; at most N per rolling hour; stops when the contract area is broken.
    /// </summary>
    public sealed class ChestPolicy
    {
        public const int FeatureVersion = 1;
        public const int LogMax = 200;
        public static readonly TimeSpan PermitLifetime = TimeSpan.FromSeconds(10);

        public bool Enabled { get; private set; }
        public bool GiftsEnabled { get; private set; }
        public readonly HashSet<string> SelectedTypes = new HashSet<string>(StringComparer.Ordinal);
        private int _delaySeconds = 3;
        public int MaxPerHour = 20;
        public bool ContractOk = true;
        public bool Connected = true;
        private readonly List<DateTime> _opened = new List<DateTime>();
        private readonly List<ChestLogEntry> _log = new List<ChestLogEntry>();

        public int DelaySeconds
        {
            get { return _delaySeconds; }
            set { _delaySeconds = Math.Max(0, Math.Min(60, value)); }
        }

        public IList<ChestLogEntry> Log { get { return _log; } }

        public static bool HasValidConsent(ConsentRecord c, string deviceId)
        {
            return c != null && c.DeviceId == deviceId && c.FeatureVersion == FeatureVersion;
        }

        /// <summary>Turning on without consent for this device and feature version fails.</summary>
        public bool TryEnable(ConsentRecord consent, string deviceId)
        {
            if (!HasValidConsent(consent, deviceId)) return false;
            Enabled = true;
            return true;
        }

        public bool TryEnableGifts(ConsentRecord consent, string deviceId)
        {
            if (!HasValidConsent(consent, deviceId)) return false;
            GiftsEnabled = true;
            return true;
        }

        public void Disable() { Enabled = false; GiftsEnabled = false; }

        public ChestStatus Status(DateTime nowUtc, bool chestReady)
        {
            if (!ContractOk) return ChestStatus.Unsupported;
            if (!Enabled) return ChestStatus.Off;
            if (!Connected || HourCount(nowUtc) >= MaxPerHour) return ChestStatus.Paused;
            return chestReady ? ChestStatus.Opening : ChestStatus.Waiting;
        }

        private int HourCount(DateTime nowUtc)
        {
            _opened.RemoveAll(t => nowUtc - t >= TimeSpan.FromHours(1));
            return _opened.Count;
        }

        /// <summary>The single decision point: may this chest be opened now?</summary>
        public bool MayOpen(string chestType, DateTime readySinceUtc, DateTime nowUtc, bool isGift)
        {
            if (!ContractOk || !Connected) return false;
            if (isGift ? !GiftsEnabled : !Enabled) return false;
            if (!SelectedTypes.Contains(chestType)) return false;
            if (nowUtc - readySinceUtc < TimeSpan.FromSeconds(_delaySeconds)) return false;
            if (HourCount(nowUtc) >= MaxPerHour) return false;
            return true;
        }

        public void Record(string chestType, bool success, string reason, DateTime nowUtc)
        {
            if (success) _opened.Add(nowUtc);
            _log.Add(new ChestLogEntry { TimeUtc = nowUtc, ChestType = chestType, Success = success, Reason = reason });
            while (_log.Count > LogMax) _log.RemoveAt(0);
        }

        /// <summary>The game component acts only on a permit refreshed in the last 10 s (app closed → stops).</summary>
        public static bool PermitValid(DateTime permitWrittenUtc, DateTime nowUtc)
        {
            return nowUtc >= permitWrittenUtc && nowUtc - permitWrittenUtc <= PermitLifetime;
        }
    }
}
