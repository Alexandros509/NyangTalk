using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using NyangTalk.App.Rendering;
using NyangTalk.Application;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.Sending;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    /// <summary>A chest shop whose state the test sets (80-SPEC 1.2 fake game model).</summary>
    public sealed class FakeShop : IChestShop
    {
        public string Kind { get; set; }
        public bool Ready { get; set; }
        public bool CanGet { get; set; }
        public bool CanAfford { get; set; }
        public bool Opening { get; set; }
        public int Clicks;
        public void Click() { Clicks++; Opening = true; }
        public FakeShop(string kind) { Kind = kind; CanGet = true; CanAfford = true; }
    }

    public static class ChestAutomatonTests
    {
        private static ChestPermit Permit(DateTime at, params string[] kinds)
        {
            var p = new ChestPermit { Enabled = true, WrittenUtc = at, DelaySeconds = 3, MaxPerHour = 20, Session = "L:1" };
            foreach (var k in kinds) p.Kinds.Add(k);
            return p;
        }

        [Test("80-1.2.1,80-1.2.2,80-1.2.3,80-4.3")]
        public static void OpensOnlyChosenReadyAffordableAfterDelay()
        {
            var a = new ChestAutomaton();
            var normal = new FakeShop("Cosmetic") { Ready = true };
            var emote = new FakeShop("Emote") { Ready = true };
            var shops = new List<IChestShop> { normal, emote };
            var t = Fake.T0;
            a.Tick(t, Permit(t, "Cosmetic"), shops, false);
            A.Eq(0, normal.Clicks, "waits for the delay");
            a.Tick(t.AddSeconds(3), Permit(t.AddSeconds(3), "Cosmetic"), shops, false);
            A.Eq(1, normal.Clicks, "opens after 3 s");
            A.Eq(0, emote.Clicks, "not chosen: never opened");
            var b = new ChestAutomaton();
            var notGettable = new FakeShop("Cosmetic") { Ready = true, CanGet = false };
            b.Tick(t, Permit(t, "Cosmetic"), new List<IChestShop> { notGettable }, false);
            b.Tick(t.AddSeconds(5), Permit(t.AddSeconds(5), "Cosmetic"), new List<IChestShop> { notGettable }, false);
            A.Eq(0, notGettable.Clicks, "CanGetChest false: no call");
            var c = new ChestAutomaton();
            var busy = new FakeShop("Cosmetic") { Ready = true };
            c.Tick(t, Permit(t, "Cosmetic"), new List<IChestShop> { busy }, true);
            c.Tick(t.AddSeconds(5), Permit(t.AddSeconds(5), "Cosmetic"), new List<IChestShop> { busy }, true);
            A.Eq(0, busy.Clicks, "exchange in progress: no call");
        }

        [Test("80-1.2.4,80-1.2.5")]
        public static void OneAtATimeAndOncePerReadyState()
        {
            var a = new ChestAutomaton();
            var normal = new FakeShop("Cosmetic") { Ready = true };
            var emote = new FakeShop("Emote") { Ready = true };
            var shops = new List<IChestShop> { normal, emote };
            var t = Fake.T0;
            for (int i = 0; i <= 4; i++) a.Tick(t.AddSeconds(i), Permit(t.AddSeconds(i), "Cosmetic", "Emote"), shops, false);
            A.Eq(1, normal.Clicks, "first chest opened");
            A.Eq(0, emote.Clicks, "second waits while the first is opening");
            normal.Opening = false; normal.Ready = true; // result unknown, still ready
            for (int i = 5; i <= 9; i++) a.Tick(t.AddSeconds(i), Permit(t.AddSeconds(i), "Cosmetic", "Emote"), shops, false);
            A.Eq(1, normal.Clicks, "same ready state: not called again");
            A.Eq(1, emote.Clicks, "then the second one");
            normal.Ready = false; a.Tick(t.AddSeconds(10), Permit(t.AddSeconds(10), "Cosmetic", "Emote"), shops, false);
            normal.Ready = true; emote.Opening = false;
            for (int i = 11; i <= 15; i++) a.Tick(t.AddSeconds(i), Permit(t.AddSeconds(i), "Cosmetic", "Emote"), shops, false);
            A.Eq(2, normal.Clicks, "ready again after being released: tried again");
        }

        [Test("80-1.2.6,80-4.4")]
        public static void PausesForPointsAndHourlyCap()
        {
            var a = new ChestAutomaton();
            var poor = new FakeShop("Cosmetic") { Ready = true, CanAfford = false };
            var t = Fake.T0;
            a.Tick(t.AddSeconds(5), Permit(t.AddSeconds(5), "Cosmetic"), new List<IChestShop> { poor }, false);
            A.Eq("paused", a.State, "paused");
            A.Eq("points", a.Reason, "not enough points");
            A.Eq(0, poor.Clicks, "no call");
            var b = new ChestAutomaton();
            var s = new FakeShop("Cosmetic");
            var p = Permit(t, "Cosmetic"); p.MaxPerHour = 2;
            int now = 0;
            for (int round = 0; round < 4; round++)
            {
                s.Ready = true; s.Opening = false;
                for (int i = 0; i < 5; i++, now++) { p.WrittenUtc = t.AddSeconds(now); b.Tick(t.AddSeconds(now), p, new List<IChestShop> { s }, false); }
                s.Ready = false; p.WrittenUtc = t.AddSeconds(now); b.Tick(t.AddSeconds(now++), p, new List<IChestShop> { s }, false);
            }
            A.Eq(2, s.Clicks, "never more than the hourly cap");
            s.Ready = true; s.Opening = false; p.WrittenUtc = t.AddSeconds(now); b.Tick(t.AddSeconds(now), p, new List<IChestShop> { s }, false);
            A.Eq("limit", b.Reason, "paused for the limit");
        }

        [Test("80-1.2.7,80-4.2")]
        public static void StopsWithoutFreshPermitOrForOtherSession()
        {
            var a = new ChestAutomaton();
            var s = new FakeShop("Cosmetic") { Ready = true };
            var t = Fake.T0;
            var p = Permit(t, "Cosmetic");
            a.Tick(t.AddSeconds(11), p, new List<IChestShop> { s }, false);   // app closed 11 s ago
            A.Eq(0, s.Clicks, "stale permit: nothing");
            A.Eq("permit", a.Reason, "paused for missing permit");
            a.Tick(t.AddSeconds(12), null, new List<IChestShop> { s }, false);
            A.Eq("off", a.State, "no permit file: off");
            A.Eq(0, s.Clicks, "never clicked");
        }

        [Test("80-4.6")]
        public static void PluginOpensChestsOnlyThroughShopClick()
        {
            var src = string.Join("\n", Directory.GetFiles(Path.Combine(Ctx.Root, "src", "Plugin"), "*.cs").Select(File.ReadAllText));
            A.True(src.Contains("\"shop.click\""), "uses the game's click handler");
            var contract = File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json"));
            A.False(contract.Contains("\"OpenChest\"") || contract.Contains("OnRemoteChestOpenRequested") || contract.Contains("SendOpenChest"), "no other chest-opening member in the contract");
        }
    }

    public static class PictureQueueTests
    {
        [Test("50-2.그림,70-2.2.2")]
        public static void OncePerKeyOnePerFrameAndThreeFailures()
        {
            var files = new HashSet<string>();
            var q = new PictureQueue { FileExists = f => files.Contains(f) };
            q.Request("5"); q.Request("5"); q.Request("7");
            A.Eq("5", q.Next(), "one per frame: first");
            q.Succeeded("5", "emotes/5.png"); files.Add("emotes/5.png");
            A.Eq("emotes/5.png", q.Request("5"), "made once, then served");
            A.Eq("7", q.Next(), "next frame: second");
            A.True(q.Next() == null, "nothing queued twice");
            for (int i = 0; i < 3; i++) { q.Failed("7"); q.Request("7"); if (i < 2) A.Eq("7", q.Next(), "retried after failure " + (i + 1)); }
            A.True(q.Next() == null, "given up after 3 failures");
            A.True(q.GivenUp("7"), "reported as given up");
            files.Clear(); // game build changed: the app removed the cache folder
            q.Request("5");
            A.Eq("5", q.Next(), "made again when the file is gone");
            q.Reset();
            q.Request("7");
            A.Eq("7", q.Next(), "a new game run tries again");
        }
    }

    public static class FilterAndPictureAppTests
    {
        [Test("50-7.필터")]
        public static void FilteredMessageShownAsNotDelivered()
        {
            var r = new Rig("filter");
            r.Step(); r.Step();
            r.Game.FilterResult = "punctuation";
            r.Chat.Send("정말요????");
            r.Step(); r.Step();
            var m = r.Chat.Outbox.Pending.Single();
            A.Eq(OutgoingState.NotDelivered, m.State, "방에 전달 안 됨");
            A.True(m.NeedsAction, "retry/delete offered");
            A.True(m.PunctuationHint, "punctuation hint");
            r.Game.FilterResult = null;
            r.Clock.Now = r.Clock.Now.AddSeconds(2);
            r.Chat.Send("보통 글");
            r.Step(); r.Step();
            A.False(r.Chat.Outbox.Pending.Any(p => p.Text == "보통 글"), "passing text: normal flow (merged)");
            A.True(Punctuation.HasRun("a----b", 3) && !Punctuation.HasRun("a---b?!?", 3) && !Punctuation.HasRun("...", 3), "4 in a row only");
            using (var meas = new Measurer())
            {
                var row = new ChatLayout(meas).OutgoingRow(m, 400, new RenderStyle());
                var status = row.Parts.First(p => p.Kind == PartKind.Status).Text;
                A.True(status.Contains("방에 전달 안 됨") && status.Contains("채팅 필터") && status.Contains("3개까지"), "notice text: " + status);
            }
        }

        [Test("50-2.그림")]
        public static void EmotePictureOrNameShown()
        {
            var dir = Ctx.NewDir("emotepic");
            var png = Path.Combine(dir, "5.png");
            using (var b = new Bitmap(64, 64)) { using (var g = Graphics.FromImage(b)) g.Clear(Color.Orange); b.Save(png, ImageFormat.Png); }
            var info = new Dictionary<string, EmoteInfo> { { "5", new EmoteInfo { File = png, Name = "하트" } }, { "6", new EmoteInfo { File = null, Name = null } } };
            var items = Fake.Build(new[] { Fake.Emote("a", "5", 0), Fake.Emote("a", "5", 1), Fake.Emote("b", "6", 2) });
            using (var meas = new Measurer())
            {
                var layout = new ChatLayout(meas) { EmoteOf = id => info.ContainsKey(id) ? info[id] : null };
                var s = new RenderStyle();
                var groups = items.OfType<EmoteGroupItem>().ToList();
                var withPic = layout.Lay(groups[0], 400, s);
                A.True(withPic.Parts.Any(p => p.Kind == PartKind.Image && p.Text == png), "picture drawn");
                A.True(withPic.Parts.Any(p => p.Kind == PartKind.Text && p.Text == "×2"), "×2 next to it");
                A.True(withPic.Tooltip.Contains("하트"), "name on hover");
                var noPic = layout.Lay(groups[1], 400, s);
                A.False(noPic.Parts.Any(p => p.Kind == PartKind.Image), "no picture");
                A.True(noPic.Parts.Any(p => p.Kind == PartKind.Text && p.Text.Contains("이모티콘 #6")), "fallback name 이모티콘 #번호");
            }
        }
    
        [Test("80-1.2.1,80-4.1")]
        public static void PermitNeedsSwitchConsentAndConnection()
        {
            using (var rig = new UiRig("permit"))
            {
                var permit = Path.Combine(rig.Dir, "exchange", NyangTalk.Shared.Exchange.PermitFile);
                rig.Host.Prefs.Settings.Set("auto.chest", true);   // switch on, but no consent yet
                rig.Step(3);
                A.False(File.Exists(permit), "no consent: no permit");
                rig.Host.Docs.Save("device", new Dictionary<string, object> { { "id", "dev1" } });
                rig.Host.Docs.Save("automation-consent", new Dictionary<string, object> { { "device", "dev1" }, { "feature_version", (long)NyangTalk.Domain.Automation.ChestPolicy.FeatureVersion } });
                rig.Step(3);
                A.True(File.Exists(permit), "permit written");
                var d = Json.Obj(Json.Parse(File.ReadAllText(permit)));
                A.Eq(rig.Game.LobbyId + ":fake-session", Json.Str(d, "session"), "tied to this lobby and game run");
                A.True(Json.Bool(Json.Obj(d["chest"]), "enabled", false), "enabled");
                rig.Host.Prefs.Settings.Set("auto.chest", false);
                rig.Step(3);
                A.False(Json.Bool(Json.Obj(Json.Obj(Json.Parse(File.ReadAllText(permit)))["chest"]), "enabled", true), "switched off at once");
            }
        }
    }
}
