using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public sealed class FakeGift : IGiftChest
    {
        public string Member { get; set; }
        public string Kind { get; set; }
        public bool Ready { get; set; }
        public bool CanAfford { get; set; }
        public int Clicks;
        public void Click() { Clicks++; }
        public FakeGift(string member, string kind = "Cosmetic") { Member = member; Kind = kind; Ready = true; CanAfford = true; }
    }

    /// <summary>80-SPEC 2장 (2026-11-19): opening other members' ready chests with my points, fake game model.</summary>
    public static class GiftTests
    {
        private static ChestPermit P(DateTime at, bool own = false, bool gifts = true, string session = "L:1")
        {
            var p = new ChestPermit { Enabled = own, Gifts = gifts, WrittenUtc = at, DelaySeconds = 3, MaxPerHour = 20, Session = session };
            p.Kinds.Add("Cosmetic"); p.Kinds.Add("Emote");
            return p;
        }

        private static void Run(ChestAutomaton a, DateTime t0, int fromSec, int toSec, Func<DateTime, ChestPermit> permit, IList<IChestShop> shops, IList<IGiftChest> gifts, ICollection<string> members, bool exchanging = false)
        {
            for (int i = fromSec; i <= toSec; i++) a.Tick(t0.AddSeconds(i), permit(t0.AddSeconds(i)), shops, exchanging, gifts, "me", members);
        }

        [Test("80-2.1,80-2.2")]
        public static void OpensOnlyOtherPresentMembersWithPermit()
        {
            var a = new ChestAutomaton();
            var mine = new FakeGift("me");
            var other = new FakeGift("p2");
            var left = new FakeGift("gone");
            var members = new HashSet<string> { "me", "p2" };
            var t = Fake.T0;
            Run(a, t, 0, 2, x => P(x, gifts: false), new List<IChestShop>(), new List<IGiftChest> { mine, other, left }, members);
            A.Eq(0, other.Clicks, "switch off: nothing");
            Run(a, t, 20, 30, x => P(x.AddSeconds(-11)), new List<IChestShop>(), new List<IGiftChest> { other }, members);
            A.Eq(0, other.Clicks, "stale permit (app closed > 10 s): nothing");
            Run(a, t, 40, 45, x => P(x), new List<IChestShop>(), new List<IGiftChest> { mine, left, other }, members);
            A.Eq(0, mine.Clicks, "never my own cat");
            A.Eq(0, left.Clicks, "never someone who left");
            A.Eq(1, other.Clicks, "other member's ready chest opened once");
        }

        [Test("80-2.3")]
        public static void WaitsForPointsAndForOwnChestOrExchange()
        {
            var a = new ChestAutomaton();
            var poor = new FakeGift("p2") { CanAfford = false };
            var members = new HashSet<string> { "me", "p2" };
            var t = Fake.T0;
            Run(a, t, 0, 5, x => P(x), new List<IChestShop>(), new List<IGiftChest> { poor }, members);
            A.Eq("paused", a.State, "paused"); A.Eq("points", a.Reason, "not enough points");
            A.Eq(0, poor.Clicks, "no call");
            var b = new ChestAutomaton();
            var own = new FakeShop("Cosmetic") { Ready = true };
            var gift = new FakeGift("p2");
            Run(b, t, 0, 4, x => P(x, own: true), new List<IChestShop> { own }, new List<IGiftChest> { gift }, members);
            A.Eq(1, own.Clicks, "my chest first");
            A.Eq(0, gift.Clicks, "gift waits while my chest is opening (shared turn)");
            own.Opening = false; own.Ready = false;
            Run(b, t, 5, 9, x => P(x, own: true), new List<IChestShop> { own }, new List<IGiftChest> { gift }, members, exchanging: true);
            A.Eq(0, gift.Clicks, "waits during the game's exchange");
            Run(b, t, 10, 14, x => P(x, own: true), new List<IChestShop> { own }, new List<IGiftChest> { gift }, members);
            A.Eq(1, gift.Clicks, "then the gift");
        }

        [Test("80-2.4,80-2.6")]
        public static void OncePerReadyStateAndSharedHourlyCap()
        {
            var a = new ChestAutomaton();
            var g = new FakeGift("p2");
            var members = new HashSet<string> { "me", "p2", "p3" };
            var t = Fake.T0;
            Run(a, t, 0, 4, x => P(x), new List<IChestShop>(), new List<IGiftChest> { g }, members);
            g.Ready = true; // result unclear, still shown as ready
            Run(a, t, 5, 9, x => P(x), new List<IChestShop>(), new List<IGiftChest> { g }, members);
            A.Eq(1, g.Clicks, "same ready state: not clicked again");
            g.Ready = false; Run(a, t, 10, 10, x => P(x), new List<IChestShop>(), new List<IGiftChest> { g }, members);
            g.Ready = true; Run(a, t, 11, 15, x => P(x), new List<IChestShop>(), new List<IGiftChest> { g }, members);
            A.Eq(2, g.Clicks, "ready again after release: tried again");
            var b = new ChestAutomaton();
            var gifts = Enumerable.Range(0, 5).Select(i => (IGiftChest)new FakeGift("p" + i)).ToList();
            var all = new HashSet<string>(gifts.Select(x => x.Member)) { "me" };
            Func<DateTime, ChestPermit> capped = x => { var p = P(x); p.MaxPerHour = 2; return p; };
            Run(b, t, 0, 60, capped, new List<IChestShop>(), gifts, all);
            A.Eq(2, gifts.Sum(x => ((FakeGift)x).Clicks), "never more than the shared hourly cap");
            A.Eq("limit", b.Reason, "paused for the limit");
        }

        [Test("80-2.5")]
        public static void RoomChangeStopsOldPermit()
        {
            var a = new ChestAutomaton();
            var g = new FakeGift("p2");
            var t = Fake.T0;
            // the component turns a permit for another lobby/game run into "disabled" before ticking (plugin ReadPermit)
            Run(a, t, 0, 5, x => P(x, gifts: false, session: "OLD:1"), new List<IChestShop>(), new List<IGiftChest> { g }, new HashSet<string> { "me", "p2" });
            A.Eq(0, g.Clicks, "permit of the previous room is not used");
            var src = File.ReadAllText(Path.Combine(Ctx.Root, "src", "Plugin", "GameLinkFeatures.cs"));
            A.True(src.Contains("permit.Session != _lobbyId + \":\" + _gameSession) { permit.Enabled = false; permit.Gifts = false; }"), "plugin drops both switches for another session");
        }

        [Test("80-2.동의,80-4.6")]
        public static void SeparateConsentAndOnlyTheClickPath()
        {
            using (var rig = new UiRig("giftpermit"))
            {
                var permit = Path.Combine(rig.Dir, "exchange", Exchange.PermitFile);
                rig.Host.Docs.Save("device", new Dictionary<string, object> { { "id", "dev1" } });
                rig.Host.Docs.Save("automation-consent", new Dictionary<string, object> { { "device", "dev1" }, { "feature_version", (long)NyangTalk.Domain.Automation.ChestPolicy.FeatureVersion } });
                rig.Host.Prefs.Settings.Set("auto.gifts", true);
                rig.Step(3);
                var d = File.Exists(permit) ? Json.Obj(Json.Parse(File.ReadAllText(permit))) : null;
                A.True(d == null || !Json.Bool(Json.Obj(d["chest"]), "gifts", false), "own-chest consent does not cover gifts");
                rig.Host.Docs.Save("automation-consent-gifts", new Dictionary<string, object> { { "device", "dev1" }, { "feature_version", (long)NyangTalk.Domain.Automation.ChestPolicy.FeatureVersion } });
                rig.Step(3);
                d = Json.Obj(Json.Parse(File.ReadAllText(permit)));
                A.True(Json.Bool(Json.Obj(d["chest"]), "gifts", false), "gift permit with its own consent");
                A.False(Json.Bool(Json.Obj(d["chest"]), "enabled", true), "own chests stay off (separate switch)");
            }
            var plugin = File.ReadAllText(Path.Combine(Ctx.Root, "src", "Plugin", "GameLinkFeatures.cs"));
            A.True(plugin.Contains("_c.Call(\"gift.click\", _o)"), "uses the chest's own click handler");
            var contract = File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json"));
            A.False(contract.Contains("TryGiftChestTo") || contract.Contains("OnRemoteChestOpenRequested") || contract.Contains("SendOpenChest"), "no other gift path in the contract");
        }
    }
}
