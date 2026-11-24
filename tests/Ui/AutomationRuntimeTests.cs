using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using NyangTalk.App;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public static class AutomationRuntimeTests
    {
        private static string PermitFile(UiRig rig) { return Path.Combine(rig.Dir, "exchange", Exchange.PermitFile); }
        private static Dictionary<string, object> Permit(UiRig rig) { return Json.Obj(Json.Parse(File.ReadAllText(PermitFile(rig)))); }
        private static Dictionary<string, object> Chest(UiRig rig) { return Json.Obj(Permit(rig)["chest"]); }

        private static void Poll(UiRig rig, double seconds = 0.01, bool ready = true, string chestState = "waiting")
        {
            rig.Clock.Now = rig.Clock.Now.AddSeconds(seconds);
            rig.Game.WriteStatus(rig.Clock.Now);
            var file = Path.Combine(rig.Dir, "exchange", Exchange.StatusFile);
            var status = Json.Obj(Json.Parse(File.ReadAllText(file)));
            status["ready"] = ready;
            status["chest"] = new Dictionary<string, object> { { "state", chestState }, { "reason", null } };
            File.WriteAllText(file, Json.Write(status));
            rig.Form.Tick();
            System.Windows.Forms.Application.DoEvents();
        }

        private static void Enable(UiRig rig, string key)
        {
            A.True(Shell.Link.RecordConsent(key == "auto.gifts" ? "automation-consent-gifts" : "automation-consent"), "fixture consent saved");
            rig.Host.Prefs.Settings.Set(key, true);
        }

        [Test("80-1.2.1,80-4.1,automation")]
        public static void OwnChestsReceiveFreshPermitsOutsideALobby()
        {
            using (var rig = new UiRig("own-outside-lobby", 0))
            {
                rig.Game.LobbyId = null;
                Enable(rig, "auto.chest"); Enable(rig, "auto.gifts");
                Poll(rig);
                A.True(Json.Bool(Chest(rig), "enabled", false), "my own chests work without a multiplayer lobby");
                A.False(Json.Bool(Chest(rig), "gifts", true), "gifts require a lobby even with separate consent");
                A.Eq(":" + rig.Game.GameSession, Json.Str(Permit(rig), "session"), "compatible with old plugin session spelling");
                A.True(Shell.Link.FeatureStatus("auto.chest").Contains(rig.Host.Prefs.L("auto.status.Waiting")), "own status follows the game report");
                A.True(Shell.Link.FeatureStatus("auto.gifts").Contains(rig.Host.Prefs.L("room.noLobby")), "gift status explains missing lobby");
                A.True(Shell.Link.AutomationStatus().Contains(rig.Host.Prefs.L("auto.status.Waiting")), "blocked gifts do not hide eligible own chests");
                var initial = Json.Str(Permit(rig), "written_utc");
                Poll(rig, 1);
                A.Eq(initial, Json.Str(Permit(rig), "written_utc"), "unchanged permits need not be written each tick");
                Poll(rig, 1.1);
                A.False(initial == Json.Str(Permit(rig), "written_utc"), "active outside-lobby permit is refreshed within its lifetime");
            }
        }

        [Test("80-2,80-4.2,automation")]
        public static void GiftPermitsRequireALobbyAndRevokeImmediatelyWhenLeaving()
        {
            using (var rig = new UiRig("gift-lobby-gate", 0))
            {
                rig.Game.LobbyId = null; Enable(rig, "auto.gifts"); Poll(rig);
                A.False(File.Exists(PermitFile(rig)), "gifts alone never get an enabled permit outside a lobby");
                A.True(Shell.Link.AutomationStatus().Contains(rig.Host.Prefs.L("room.noLobby")), "sidebar explains why gifts are paused");
                rig.Game.LobbyId = "room-a"; Poll(rig);
                A.True(Json.Bool(Chest(rig), "gifts", false), "joining a lobby enables separately consented gifts");
                A.False(Json.Bool(Chest(rig), "enabled", true), "gift consent does not enable own chests");
                A.Eq("room-a:" + rig.Game.GameSession, Json.Str(Permit(rig), "session"), "bound to current lobby");
                rig.Game.LobbyId = null; Poll(rig);
                A.False(Json.Bool(Chest(rig), "gifts", true), "leaving revokes gifts without waiting for the refresh interval");
                A.Eq<string>(null, Json.Str(Permit(rig), "session"), "disabled permit has no target session");
                rig.Game.LobbyId = "room-b"; Poll(rig);
                A.True(Json.Bool(Chest(rig), "gifts", false), "quick rejoin gets a fresh permit immediately");
                A.Eq("room-b:" + rig.Game.GameSession, Json.Str(Permit(rig), "session"), "never sends the prior room's permit");
            }
        }

        [Test("80-1.2.7,80-4.2,automation")]
        public static void PermitChangesRebindAndRevokeWithoutTheTwoSecondDelay()
        {
            using (var rig = new UiRig("permit-changes", 0))
            {
                Enable(rig, "auto.chest"); Poll(rig);
                rig.Game.LobbyId = null; Poll(rig);
                A.Eq(":" + rig.Game.GameSession, Json.Str(Permit(rig), "session"), "leaving a lobby rebinds own chest permission immediately");
                rig.Game.GameSession = "second-game-run"; Poll(rig);
                A.Eq(":second-game-run", Json.Str(Permit(rig), "session"), "game restart rebinds immediately");
                rig.Host.Prefs.Settings.Set("auto.chestTypes", "emote");
                rig.Host.Prefs.Settings.Set("auto.chestDelay", 17);
                rig.Host.Prefs.Settings.Set("auto.chestMaxPerHour", 7); Poll(rig);
                A.Eq(17L, Json.Long(Chest(rig), "delay", 0), "new delay is used immediately");
                A.Eq(7L, Json.Long(Chest(rig), "max_per_hour", 0), "new cap is used immediately");
                A.Eq(1, Json.List(Chest(rig), "kinds").Count, "removed chest kind is revoked immediately");
                A.Eq("Emote", Json.List(Chest(rig), "kinds")[0] as string, "only the selected kind remains");
                rig.Host.Prefs.Settings.Set("auto.chest", false); Poll(rig);
                A.False(Json.Bool(Chest(rig), "enabled", true), "off revokes immediately");
                rig.Host.Prefs.Settings.Set("auto.chest", true); Poll(rig);
                A.True(Json.Bool(Chest(rig), "enabled", false), "quick on after off does not wait behind an off write");
                rig.Game.GameSession = null; Poll(rig);
                A.False(Json.Bool(Chest(rig), "enabled", true), "unknown game run is never authorized");
            }
        }

        [Test("80-4.1,80-4.2,automation")]
        public static void ConsentConnectionAndSupportedGameStateRemainRequired()
        {
            using (var rig = new UiRig("permit-gates", 0))
            {
                rig.Host.Prefs.Settings.Set("auto.chest", true); Poll(rig);
                A.False(File.Exists(PermitFile(rig)), "no consent means no permit");
                A.True(Shell.Link.FeatureStatus("auto.chest").Contains(rig.Host.Prefs.L("auto.reason.permit")), "missing consent is not mislabeled off");
                Enable(rig, "auto.chest"); Poll(rig);
                A.True(Json.Bool(Chest(rig), "enabled", false), "valid consent permits own chests");
                Poll(rig, 0.01, false);
                A.False(Json.Bool(Chest(rig), "enabled", true), "not-ready game revokes promptly");
                Poll(rig); rig.Game.Areas["Chest"] = false; Poll(rig);
                A.False(Json.Bool(Chest(rig), "enabled", true), "unsupported chest area revokes promptly");
                rig.Game.Areas["Chest"] = true; Poll(rig);
                rig.Clock.Now = rig.Clock.Now.AddSeconds(7); rig.Form.Tick();
                A.False(Json.Bool(Chest(rig), "enabled", true), "stale status revokes permission");
                var revoked = File.ReadAllText(PermitFile(rig));
                rig.Clock.Now = rig.Clock.Now.AddSeconds(20); rig.Form.Tick();
                A.Eq(revoked, File.ReadAllText(PermitFile(rig)), "disconnected polling never renews an enabled permit");
            }
        }
    }
}
