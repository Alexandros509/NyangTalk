using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NyangTalk.Domain.Appearance;
using NyangTalk.Domain.Automation;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;
using NyangTalk.Domain.Lobby;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;
using NyangTalk.Domain.Sending;
using NyangTalk.Domain.Update;

namespace NyangTalk.Tests
{
    public static class VersionTests
    {
        [Test("01-2.2,20-9.14")]
        public static void SemVerPrecedenceExample()
        {
            var order = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0" };
            for (int i = 0; i < order.Length - 1; i++)
                A.True(SemVersion.Parse(order[i]) < SemVersion.Parse(order[i + 1]), order[i] + " < " + order[i + 1]);
            A.True(SemVersion.Parse("1.0.0") < SemVersion.Parse("2.0.0") && SemVersion.Parse("2.0.0") < SemVersion.Parse("2.1.0") && SemVersion.Parse("2.1.0") < SemVersion.Parse("2.1.1"), "core order");
            A.True(SemVersion.Parse("1.3.0+build.1") == SemVersion.Parse("1.3.0"), "build metadata ignored");
            A.True(SemVersion.Parse("1.3.0-beta.2") < SemVersion.Parse("1.3.0"), "beta < release");
            foreach (var bad in new[] { "1.4", "v1.4.0", "1.4.0.0", "01.4.0", "1.4.0-", "1.4.0-01", "", " 1.4.0" })
            {
                SemVersion v;
                A.False(SemVersion.TryParse(bad, out v), "reject " + bad);
            }
            A.Eq("1.3.0.0", SemVersion.Parse("1.3.0-beta.2").ToFileVersion(), "file version");
            A.True(VersionRange.Parse(">=1.2.0 <2.0.0").Contains(SemVersion.Parse("1.9.9")), "in range");
            A.False(VersionRange.Parse(">=1.2.0 <2.0.0").Contains(SemVersion.Parse("2.0.0")), "out of range");
        }
    }

    public static class PeopleTests
    {
        [Test("60-5.1")]
        public static void DisplayNameModes()
        {
            A.Eq("별명", DisplayName.Format("Orig", "별명", DisplayNameMode.AliasOnly), "alias only");
            A.Eq("별명 (Orig)", DisplayName.Format("Orig", "별명", DisplayNameMode.AliasThenOriginal), "alias (orig)");
            A.Eq("Orig (별명)", DisplayName.Format("Orig", "별명", DisplayNameMode.OriginalThenAlias), "orig (alias)");
            A.Eq("Orig", DisplayName.Format("Orig", null, DisplayNameMode.AliasOnly), "no alias");
        }

        [Test("60-5.2")]
        public static void AliasKeyedBySteamId()
        {
            var book = new AliasBook();
            A.Eq(AliasError.None, book.Set("76561190000000001", "  친구  "), "trimmed");
            A.Eq("친구", book.Get("76561190000000001"), "stored trimmed");
            var renamed = new ChatMessage { SenderId = "76561190000000001", SenderName = "NewName" };
            A.Eq("친구 (NewName)", DisplayName.Format(renamed.SenderName, book.Get(renamed.SenderId), DisplayNameMode.AliasThenOriginal), "alias survives rename");
            string c;
            A.Eq(AliasError.Empty, AliasBook.Validate("   ", out c), "empty");
            A.Eq(AliasError.TooLong, AliasBook.Validate(new string('가', 21), out c), "21 chars");
            A.Eq(AliasError.None, AliasBook.Validate(new string('가', 20), out c), "20 chars ok");
            A.Eq(AliasError.HasNewline, AliasBook.Validate("a\nb", out c), "newline");
        }

        [Test("60-5.3")]
        public static void AutoColorsReadableInAllThemes()
        {
            foreach (var id in Theme.Ids)
            foreach (var dark in new[] { false, true })
            {
                var th = Theme.Get(id, dark);
                foreach (var p in PersonColors.Palette)
                {
                    // name text on the window (single layout: names sit on window background) and on the bubble
                    var onWindow = ColorMath.EnsureTextContrast(p, th.Window);
                    A.True(ColorMath.Contrast(onWindow, th.Window) >= 4.5, id + " name on window " + p);
                    var onBubble = ColorMath.EnsureTextContrast(p, th.OtherBubble);
                    A.True(ColorMath.Contrast(onBubble, th.OtherBubble) >= 4.5, id + " name on bubble " + p);
                    var tint = ColorMath.Tint(p, th.OtherBubble, th.OtherText);
                    A.True(ColorMath.Contrast(th.OtherText, tint) >= 4.5, id + " body on tint " + p);
                }
            }
        }

        [Test("60-5.5")]
        public static void ColorNoneDisablesPlacementAndMineKeepsTheme()
        {
            var pc = new PersonColors { Mode = PersonColorMode.None, Placement = ColorPlacement.Both };
            A.False(pc.PlacementEnabled, "placement disabled");
            A.False(pc.ColorsName || pc.ColorsBackground, "nothing coloured");
            pc.Mode = PersonColorMode.Auto;
            foreach (ColorPlacement pl in Enum.GetValues(typeof(ColorPlacement)))
            {
                pc.Placement = pl;
                A.True(pc.BaseColor("x", true) == null, "mine has no person colour in " + pl);
            }
            A.Eq(PersonColors.AutoIndex("76561190000000001"), PersonColors.AutoIndex("76561190000000001"), "stable auto colour");
            pc.Mode = PersonColorMode.Manual;
            pc.Manual["x"] = 3;
            A.Eq(PersonColors.Palette[3].ToHex(), pc.BaseColor("x", false).Value.ToHex(), "manual colour");
            A.Eq(PersonColors.Palette[PersonColors.AutoIndex("y")].ToHex(), pc.BaseColor("y", false).Value.ToHex(), "unassigned falls back to auto");
        }

        [Test("90-1,90-1.6")]
        public static void ThemesTextContrast()
        {
            A.Eq(6, Theme.Ids.Length, "six themes");
            int combos = 0;
            foreach (var id in Theme.Ids)
                foreach (var dark in new[] { false, true })
                {
                    var th = Theme.Get(id, dark);
                    A.Eq(dark, th.Dark, id + " variant flag");
                    A.Eq(dark, ColorMath.Luminance(th.Window) < 0.2, id + (dark ? " dark" : " light") + " window brightness");
                    foreach (var pair in th.TextPairs())
                    {
                        var contrast = ColorMath.Contrast(pair.Value[0], pair.Value[1]);
                        // The user explicitly chose the reference's legacy light palette, including its secondary colours.
                        // Exact role values are checked independently by StyleReferenceTests, rather than silently recoloured.
                        bool legacySecondary = id == "default" && !dark && (pair.Key.StartsWith("subtext/") || pair.Key == "notice/pill" || pair.Key == "warning/window");
                        if (legacySecondary) Ctx.Measurements.Add("기본 밝게 원본 유지: " + pair.Key + " 대비 " + contrast.ToString("0.00") + ":1 (4.5 기준 예외)");
                        else A.True(contrast >= 4.5, id + (dark ? "/dark " : " ") + pair.Key + " " + contrast.ToString("0.00"));
                    }
                    combos++;
                }
            A.Eq(12, combos, "6 themes x light/dark");
        }

        [Test("90-1.5")]
        public static void WindowSizeSteps()
        {
            A.Eq("300x360,380x520,510x790,680x900", string.Join(",", WindowSizes.Steps.Select(s => string.Join("x", WindowSizes.Logical(s)))), "logical sizes");
            A.Eq("375x450", string.Join("x", WindowSizes.Pixels("mini", 120)), "mini at 125%");
            A.Eq("765x1185", string.Join("x", WindowSizes.Pixels("default", 144)), "default at 150%");
            A.Eq("680x900", string.Join("x", WindowSizes.Pixels("large", 96)), "large at 100%");
            A.True(WindowSizes.Pixels(WindowSizes.Custom, 96) == null, "custom is only shown, never applied");
            A.Eq("default", Settings.Def("screen.windowSize").Default, "default step");
        }
    }

    public static class SendingTests
    {
        [Test("50-7")]
        public static void SendChecks()
        {
            var o = new Outbox();
            var now = Fake.T0;
            A.Eq(SendBlock.TooLong, o.Check(new string('a', 101), 100, true, true, false, now), "over limit");
            A.Eq(SendBlock.None, o.Check(new string('a', 100), 100, true, true, false, now), "at limit");
            A.Eq(SendBlock.Disconnected, o.Check("hi", 100, false, true, false, now), "disconnected");
            A.Eq(SendBlock.Archived, o.Check("hi", 100, true, true, true, now), "archived");
            A.Eq(SendBlock.Empty, o.Check("  ", 100, true, true, false, now), "empty");
            o.Submit("r1", "hi", now);
            A.Eq(SendBlock.Duplicate, o.Check("hi", 100, true, true, false, now.AddMilliseconds(900)), "dup within 1 s");
            A.Eq(SendBlock.None, o.Check("hi", 100, true, true, false, now.AddMilliseconds(1100)), "after 1 s ok");
        }

        [Test("50-7")]
        public static void StatesAndTimeout()
        {
            var o = new Outbox();
            var m = o.Submit("r1", "hello", Fake.T0, "L1", "S1");
            A.Eq(OutgoingState.Sending, m.State, "sending");
            o.ApplyResult("r1", SendResultKind.Delivered, null);
            A.Eq(OutgoingState.Delivered, m.State, "delivered");
            var echo = Fake.Text("me", "hello", 2, true); echo.GameSession = "S1";
            A.True(o.OnEcho(echo, "me") == m, "echo merges");
            A.Eq(OutgoingState.Merged, m.State, "merged");
            A.Eq(0, o.Pending.Count, "pending cleared");
            var m2 = o.Submit("r2", "later", Fake.T0);
            o.Tick(Fake.T0.AddSeconds(9));
            A.Eq(OutgoingState.Sending, m2.State, "9 s still sending");
            o.Tick(Fake.T0.AddSeconds(11));
            A.Eq(OutgoingState.Unconfirmed, m2.State, "10 s → unconfirmed");
            var m3 = o.Submit("r3", "delivered only", Fake.T0);
            o.ApplyResult("r3", SendResultKind.Delivered, null);
            o.Tick(Fake.T0.AddSeconds(11));
            A.Eq(OutgoingState.Unconfirmed, m3.State, "delivered but not back in the room within 10 s → unconfirmed");
        }

        private static ChatMessage Back(string sender, string body, double seconds, string lobby = "L1", string session = "S1")
        {
            var m = Fake.Text(sender, body, seconds, sender == "me"); m.LobbyId = lobby; m.GameSession = session; return m;
        }

        [Test("50-7.짝짓기")]
        public static void StrictPairing()
        {
            var o = new Outbox();
            var a = o.Submit("a", "같은 글", Fake.T0, "L1", "S1");
            var b = o.Submit("b", "같은 글", Fake.T0.AddSeconds(2), "L1", "S1");
            A.True(o.OnEcho(Back("other", "같은 글", 3), "me") == null, "someone else's same text: not paired");
            A.True(o.OnEcho(Back("me", "같은 글", 3, "L2"), "me") == null, "other lobby: not paired");
            A.True(o.OnEcho(Back("me", "같은 글", 3, "L1", "S0"), "me") == null, "other game run: not paired");
            A.True(o.OnEcho(Back("me", "같은 글", 3), null) == null, "my id unknown: not paired");
            A.True(o.OnEcho(Back("me", "같은 글", 4), "me") == a, "first received → first sent");
            A.True(o.OnEcho(Back("me", "같은 글", 5), "me") == b, "second received → second sent");
            A.True(o.OnEcho(Back("me", "같은 글", 6), "me") == null, "a third copy pairs with nothing (shown as a normal message)");
            var late = o.Submit("c", "늦은 글", Fake.T0.AddSeconds(10), "L1", "S1");
            o.Tick(Fake.T0.AddSeconds(25));
            A.Eq(OutgoingState.Unconfirmed, late.State, "unconfirmed after 10 s");
            A.True(o.OnEcho(Back("me", "늦은 글", 10 + 121), "me") == null, "arrives after 2 min: not paired");
            A.Eq(OutgoingState.Unconfirmed, late.State, "stays 확인 못 함 with retry/delete");
            var ok = o.Submit("d", "돌아온 글", Fake.T0.AddSeconds(200), "L1", "S1");
            o.Tick(Fake.T0.AddSeconds(215));
            A.Eq(OutgoingState.Unconfirmed, ok.State, "unconfirmed first");
            A.True(o.OnEcho(Back("me", "돌아온 글", 200 + 100), "me") == ok, "unconfirmed, then back within 2 min: merged");
            var early = o.Submit("e", "이른 글", Fake.T0.AddSeconds(400), "L1", "S1");
            A.True(o.OnEcho(Back("me", "이른 글", 398.5), "me") == null, "more than 1 s before sending: not paired");
            A.True(o.OnEcho(Back("me", "이른 글", 399.5), "me") == early, "within 1 s before: paired");
        }

        [Test("50-9.8")]
        public static void RejectRetryKeepsDraft()
        {
            var o = new Outbox();
            o.Submit("r1", "first", Fake.T0);
            o.ApplyResult("r1", SendResultKind.Rejected, "too fast");
            var draft = "새로 쓰던 글"; // the composer's draft is a separate value and must not change
            A.True(o.Pending.Single().NeedsAction, "rejected stays with actions");
            var text = o.TakeForRetry("r1");
            A.Eq("first", text, "retry text");
            o.Submit("r2", text, Fake.T0.AddSeconds(5));
            o.ApplyResult("r2", SendResultKind.Delivered, null);
            A.Eq("새로 쓰던 글", draft, "draft preserved");
            A.Eq(OutgoingState.Delivered, o.Find("r2").State, "retry delivered");
        }

        [Test("50-7")]
        public static void SendLogCappedAt500()
        {
            var o = new Outbox();
            for (int i = 0; i < 520; i++) o.Submit("r" + i, "t" + i, Fake.T0.AddSeconds(i));
            A.Eq(500, o.Log.Count, "log 500");
            A.Eq("t20", o.Log[0].Text, "oldest dropped");
        }
    }

    public static class LobbyTests
    {
        private static List<Participant> P(params string[] ids) { return ids.Select(i => new Participant { SteamId = i, Name = "n" + i, IsMe = i == "me" }).ToList(); }

        [Test("60-5.7")]
        public static void UnreadableParticipantsNotGuessed()
        {
            var seen = new[] { Fake.Text("a", "x", 0), Fake.Text("b", "y", 1), Fake.Text("a", "z", 2) };
            var r = ParticipantList.Resolve(null, false, seen);
            A.Eq(2, r.Count, "only senders");
            A.True(r.All(p => !p.IsOwner), "no guessed owner");
            var faces = ParticipantList.HeaderFaces(P("1", "2", "3", "4", "5"));
            A.Eq(3, faces.Count, "max 3 faces");
        }

        [Test("40-2")]
        public static void LobbyCodeDisplay()
        {
            A.Eq(null, new LobbyInfo { Code = "ABCD", CodeHidden = true }.DisplayCode, "hidden");
            A.Eq(null, new LobbyInfo { Code = "" }.DisplayCode, "empty");
            A.Eq("ABCD", new LobbyInfo { Code = "ABCD" }.DisplayCode, "shown");
            A.Eq("456789", LobbyInfo.IdSuffix("109775240123456789"), "id suffix");
        }
    }

    public static class AppearanceTests
    {
        [Test("70-3.4,70-3.5")]
        public static void PortraitCachePolicy()
        {
            var c = new PortraitCache();
            var look = new AppearanceKey("skin1", "hat1");
            A.True(c.SetLook("a", look, Fake.T0), "first needs render");
            A.False(c.SetLook("b", new AppearanceKey("skin1", "hat1"), Fake.T0), "same look: no second render");
            c.OnRendered(look, "f1.png", Fake.T0);
            A.Eq(1, c.RenderedCount, "one render");
            var newLook = new AppearanceKey("skin2", "hat1");
            A.True(c.SetLook("a", newLook, Fake.T0.AddSeconds(1)), "changed look renders");
            c.OnRenderFailed(newLook);
            A.Eq("f1.png", c.For("a").File, "failure keeps last good picture");
            A.True(c.For("z") == null, "unknown → initial circle");
            var files = c.LeaveRoom();
            A.True(files.Contains("f1.png"), "leaving deletes pictures");
            A.Eq(0, c.RenderedCount, "cache empty");
        }

        [Test("70-2.2")]
        public static void ReplacedPortraitGrace()
        {
            var c = new PortraitCache();
            var l1 = new AppearanceKey("s", "1"); var l2 = new AppearanceKey("s", "2");
            c.SetLook("a", l1, Fake.T0); c.OnRendered(l1, "1.png", Fake.T0);
            c.SetLook("a", l2, Fake.T0.AddSeconds(1)); c.OnRendered(l2, "2.png", Fake.T0.AddSeconds(1));
            A.Eq(0, c.Sweep(Fake.T0.AddSeconds(20)).Count, "within 30 s kept");
            A.True(c.Sweep(Fake.T0.AddSeconds(32)).Contains("1.png"), "after 30 s removed");
        }
    }

    public static class AutomationTests
    {
        [Test("80-4.1,80-4.3,80-4.4,80-4.5")]
        public static void ChestRules()
        {
            var p = new ChestPolicy();
            A.False(p.TryEnable(null, "dev"), "no consent: stays off");
            A.False(p.TryEnable(new ConsentRecord { DeviceId = "other", FeatureVersion = 1 }, "dev"), "other device consent");
            A.False(p.TryEnable(new ConsentRecord { DeviceId = "dev", FeatureVersion = 0 }, "dev"), "old feature version");
            A.True(p.TryEnable(new ConsentRecord { DeviceId = "dev", FeatureVersion = ChestPolicy.FeatureVersion }, "dev"), "consented");
            p.SelectedTypes.Add("normal");
            var now = Fake.T0;
            A.False(p.MayOpen("emote", now.AddSeconds(-10), now, false), "unselected type");
            A.False(p.MayOpen("normal", now.AddSeconds(-2), now, false), "3 s delay");
            A.True(p.MayOpen("normal", now.AddSeconds(-3), now, false), "after delay");
            A.False(p.MayOpen("normal", now.AddSeconds(-3), now, true), "gift switch separate");
            p.MaxPerHour = 2;
            p.Record("normal", true, null, now); p.Record("normal", true, null, now.AddMinutes(1));
            A.False(p.MayOpen("normal", now, now.AddMinutes(2), false), "hourly cap");
            A.True(p.MayOpen("normal", now, now.AddMinutes(61), false), "rolling hour frees");
            p.ContractOk = false;
            A.Eq(ChestStatus.Unsupported, p.Status(now, true), "broken contract");
            A.False(p.MayOpen("normal", now, now.AddHours(3), false), "nothing opens when unsupported");
            p.DelaySeconds = 100; A.Eq(60, p.DelaySeconds, "delay clamp");
        }

        [Test("80-4.2")]
        public static void PermitExpires()
        {
            A.True(ChestPolicy.PermitValid(Fake.T0, Fake.T0.AddSeconds(10)), "10 s ok");
            A.False(ChestPolicy.PermitValid(Fake.T0, Fake.T0.AddSeconds(10.5)), "stale permit");
            var p = new ChestPolicy();
            for (int i = 0; i < 230; i++) p.Record("n", false, "x", Fake.T0);
            A.Eq(200, p.Log.Count, "log 200");
        }
    }

    public static class GameLinkDomainTests
    {
        [Test("40-7.9")]
        public static void ExchangeCompatibilityRules()
        {
            A.Eq(ExchangeVerdict.Ok, ExchangeCompatibility.Check("1.0.0", "0.1.0"), "ok");
            A.Eq(ExchangeVerdict.Ok, ExchangeCompatibility.Check("1.4.0", "0.1.5"), "higher minor ok");
            A.Eq(ExchangeVerdict.SchemaMajorMismatch, ExchangeCompatibility.Check("2.0.0", "0.1.0"), "major differs");
            A.Eq(ExchangeVerdict.ProducerOutOfRange, ExchangeCompatibility.Check("1.0.0", "0.2.0"), "producer out of range");
            A.Eq(LinkState.Connected, LinkHealth.Evaluate(true, Fake.T0, ExchangeVerdict.Ok, Fake.T0.AddSeconds(6)), "6 s ok");
            A.Eq(LinkState.Disconnected, LinkHealth.Evaluate(true, Fake.T0, ExchangeVerdict.Ok, Fake.T0.AddSeconds(6.1)), "over 6 s");
            A.Eq(LinkState.NeedsReinstall, LinkHealth.Evaluate(true, Fake.T0, ExchangeVerdict.ProducerOutOfRange, Fake.T0), "reinstall");
        }

        [Test("10-11.7")]
        public static void SendBrokenLocksOnlyInput()
        {
            var a = new AreaAvailability();
            a.SetStatic(FeatureArea.ChatSend, false);
            A.True(a.SendLocked, "send locked");
            A.True(a.ReceiveWorks, "receive continues");
            a.SetRuntime(FeatureArea.ChatSend, true);
            A.False(a.SendLocked, "plugin's runtime verdict wins");
        }

        [Test("10-7")]
        public static void NoticeShownOnce()
        {
            var d = new NoticeDeduper();
            A.True(d.ShouldShow("fp1", "send-broken"), "first");
            A.False(d.ShouldShow("fp1", "send-broken"), "repeat");
            A.True(d.ShouldShow("fp2", "send-broken"), "new fingerprint");
        }

        [Test("40-1.1")]
        public static void GameLogMarkers()
        {
            A.True(GameLogScanner.HasTypeLoadProblem(new[] { "[Error] System.TypeLoadException: Could not load type" }), "TypeLoadException");
            A.True(GameLogScanner.HasTypeLoadProblem(new[] { "VTable setup of type X failed" }), "vtable");
            A.False(GameLogScanner.HasTypeLoadProblem(new[] { "[Info] all good" }), "clean log");
        }
    }

    public static class UpdatePolicyTests
    {
        private static ReleaseInfo Info(string v, long seq, string channel = "stable", string product = "nyangtalk")
        {
            return new ReleaseInfo { Product = product, Channel = channel, Version = SemVersion.Parse(v), Sequence = seq };
        }

        [Test("20-9.4,20-9.5,20-9.14")]
        public static void Decisions()
        {
            var cur = SemVersion.Parse("0.1.0");
            A.Eq(UpdateDecision.NewVersion, UpdatePolicy.Decide(Info("0.2.0", 5), cur, "stable", 4, null), "newer");
            A.Eq(UpdateDecision.NotNewer, UpdatePolicy.Decide(Info("0.1.0", 5), cur, "stable", 4, null), "same");
            A.Eq(UpdateDecision.NotNewer, UpdatePolicy.Decide(Info("0.0.9", 5), cur, "stable", 4, null), "lower");
            A.Eq(UpdateDecision.NotNewer, UpdatePolicy.Decide(Info("0.1.0+build.7", 5), cur, "stable", 4, null), "build metadata only");
            A.Eq(UpdateDecision.SequenceRollback, UpdatePolicy.Decide(Info("0.3.0", 3), cur, "stable", 4, null), "lower sequence");
            A.Eq(UpdateDecision.WrongProduct, UpdatePolicy.Decide(Info("0.3.0", 5, "stable", "other"), cur, "stable", 4, null), "product");
            A.Eq(UpdateDecision.WrongChannel, UpdatePolicy.Decide(Info("0.3.0", 5, "beta"), cur, "stable", 4, null), "channel");
            A.Eq(UpdateDecision.PrereleaseOnStable, UpdatePolicy.Decide(Info("1.4.0-beta.1", 5), cur, "stable", 4, null), "stable gets no beta");
            A.Eq(UpdateDecision.NewVersion, UpdatePolicy.Decide(Info("1.4.0-beta.1", 5, "beta"), cur, "beta", 4, null), "beta channel");
            A.Eq(UpdateDecision.BlockedAfterRollback, UpdatePolicy.Decide(Info("0.2.0", 5), cur, "stable", 4, SemVersion.Parse("0.2.0")), "rolled back version not retried");
        }

        [Test("20-9.6")]
        public static void RedirectRules()
        {
            var hosts = new[] { "github.com", "objects.githubusercontent.com" };
            A.True(UpdatePolicy.RedirectAllowed(new Uri("https://objects.githubusercontent.com/x"), 1, hosts), "allowed");
            A.False(UpdatePolicy.RedirectAllowed(new Uri("http://objects.githubusercontent.com/x"), 1, hosts), "http");
            A.False(UpdatePolicy.RedirectAllowed(new Uri("https://evil.example/x"), 1, hosts), "other host");
            A.False(UpdatePolicy.RedirectAllowed(new Uri("https://github.com/x"), 6, hosts), "too many hops");
        }

        [Test("20-9.10")]
        public static void RestartDeferred()
        {
            A.False(UpdatePolicy.CanRestartNow(true, false, false), "draft");
            A.False(UpdatePolicy.CanRestartNow(false, true, false), "pending send");
            A.False(UpdatePolicy.CanRestartNow(false, false, true), "installing");
            A.True(UpdatePolicy.CanRestartNow(false, false, false), "free");
        }
    }

    public static class PreferenceTests
    {
        [Test("30-8.2")]
        public static void LayoutHysteresis()
        {
            A.Eq(LayoutKind.Sidebar, LayoutResolver.Resolve("auto", 760, LayoutKind.Single), "760 → sidebar");
            A.Eq(LayoutKind.Single, LayoutResolver.Resolve("auto", 719, LayoutKind.Sidebar), "719 → single");
            A.Eq(LayoutKind.Single, LayoutResolver.Resolve("auto", 740, LayoutKind.Single), "740 keeps single");
            A.Eq(LayoutKind.Sidebar, LayoutResolver.Resolve("auto", 740, LayoutKind.Sidebar), "740 keeps sidebar");
            A.Eq(LayoutKind.Single, LayoutResolver.Resolve("single", 1900, LayoutKind.Sidebar), "fixed single");
            A.True(LayoutResolver.ForceCollapse("sidebar", 700), "fixed sidebar narrow collapses");
        }

        [Test("90-7.4")]
        public static void Retention()
        {
            A.True(RetentionPolicy.IsExpired(Fake.T0, Fake.T0.AddDays(31), 30), "31 days > 30");
            A.False(RetentionPolicy.IsExpired(Fake.T0, Fake.T0.AddDays(29), 30), "29 days kept");
            A.False(RetentionPolicy.IsExpired(Fake.T0, Fake.T0.AddDays(5000), 0), "forever");
        }

        [Test("90-7.5")]
        public static void DiagnosticsMasking()
        {
            var s = DiagnosticsSanitizer.Line(@"user 76561198012345678 at C:\Users\someone\AppData", @"C:\Users\someone");
            A.False(s.Contains("76561198012345678"), "full steam id removed");
            A.True(s.Contains("…5678"), "last 4 kept");
            A.True(s.Contains("%USERPROFILE%"), "path masked");
        }

        [Test("90-7.6,01-6")]
        public static void TranslationsComplete()
        {
            foreach (var kv in Strings.Table)
            {
                A.Eq(3, kv.Value.Length, kv.Key + " has 3 languages");
                foreach (var v in kv.Value) A.True(!string.IsNullOrWhiteSpace(v), "missing translation " + kv.Key);
                var holes = Regex.Matches(kv.Value[0], @"\{\d\}").Count;
                for (int i = 1; i < 3; i++) A.Eq(holes, Regex.Matches(kv.Value[i], @"\{\d\}").Count, kv.Key + " placeholders match in " + Strings.Languages[i]);
            }
            // Every key used in the source exists in the table.
            var used = new HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(Ctx.Root, "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"\bL\(""([a-zA-Z0-9_.]+)""")) used.Add(m.Groups[1].Value);
            // keys built at run time from a prefix: check every value of the source enum/list instead
            foreach (var a in Enum.GetNames(typeof(FeatureArea))) used.Add("area." + a);
            foreach (var b in Enum.GetNames(typeof(SendBlock))) if (b != "None") used.Add("send.blocked." + b);
            foreach (var c in new[] { "screen", "chat", "people", "char", "auto", "link", "about" }) used.Add("settings.cat." + c);
            foreach (var d in Settings.Defs) used.Add("set." + d.Key);
            used.RemoveWhere(k => k.EndsWith("."));
            var missing = used.Where(k => !Strings.Table.ContainsKey(k)).ToList();
            A.True(missing.Count == 0, "keys used but not translated: " + string.Join(", ", missing));
        }

        [Test("90-7.1")]
        public static void SettingsValidation()
        {
            var s = new Settings();
            A.False(s.Set("screen.theme", "neon"), "unknown choice rejected");
            A.True(s.Set("screen.fontSize", "12"), "int ok");
            A.False(s.Set("screen.fontSize", "99"), "int range");
            var stored = new Dictionary<string, string> { { "screen.theme", "mint" }, { "future.key", "x" }, { "chat.emoteWindow", "77" } };
            s.Load(stored);
            A.Eq("mint", s.Get("screen.theme"), "loaded");
            A.Eq("15", s.Get("chat.emoteWindow"), "invalid → default");
            A.Eq("x", s.Snapshot()["future.key"], "unknown key preserved");
        }
    }
}
