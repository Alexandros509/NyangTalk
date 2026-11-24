using System.IO;
using NyangTalk.App;
using NyangTalk.Shared;

namespace NyangTalk.Tests
{
    public static class ComponentErrorNoticeTests
    {
        private static void Poll(UiRig rig, string error)
        {
            rig.Clock.Now = rig.Clock.Now.AddSeconds(1);
            rig.Game.WriteStatus(rig.Clock.Now);
            var file = Path.Combine(rig.Dir, "exchange", Exchange.StatusFile);
            var status = Json.Obj(Json.Parse(File.ReadAllText(file))); status["error"] = error;
            File.WriteAllText(file, Json.Write(status));
            rig.Form.Tick();
        }

        [Test("40-7,95-2.6,component-io")]
        public static void HealthyStatusRemovesOldWarningAndLetsARecurrenceShowAgain()
        {
            using (var rig = new UiRig("component-error-recovery", 0))
            {
                Poll(rig, "chest: IOException");
                var key = "component-error:chest: IOException";
                A.Eq(key, rig.Form.Notices.Get(NoticeKind.Info).Key, "actual status drives the notice");
                rig.Form.Notices.Dismiss(key);
                Poll(rig, null);
                A.Eq<NoticeEntry>(null, rig.Form.Notices.Get(NoticeKind.Info), "recovery removes the stale component notice");
                Poll(rig, "chest: IOException");
                A.False(rig.Form.Notices.IsDismissed(key), "a recovered error may alert again on a new failure");
                A.Eq(key, rig.Form.Notices.Get(NoticeKind.Info).Key, "recurrence is displayed");
                rig.Form.ShowNotice("saved", "a separate saved result");
                Poll(rig, null);
                A.Eq("saved", rig.Form.Notices.Get(NoticeKind.Info).Key, "recovery preserves unrelated information");
            }
        }
    }
}
