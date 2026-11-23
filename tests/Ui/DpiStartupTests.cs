using System;
using System.IO;
using NyangTalk.Adapters.Storage;
using NyangTalk.App.Platform;

namespace NyangTalk.Tests
{
    public sealed class DisplayDomainInspector : MarshalByRefObject
    {
        public string[] Read()
        {
            return new[] { AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
                AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName, DpiStartup.Configured.ToString() };
        }
        public override object InitializeLifetimeService() { return null; }
    }

    public static class DpiStartupTests
    {
        [Test("90-1,DPI")]
        public static void UiDomainUsesItsOwnPerMonitorConfiguration()
        {
            var root = Ctx.NewDir("dpi-domain");
            var domain = DpiStartup.CreateUiDomain(typeof(DpiStartupTests).Assembly.Location, root);
            try
            {
                var inspector = (DisplayDomainInspector)domain.CreateInstanceAndUnwrap(typeof(DisplayDomainInspector).Assembly.FullName, typeof(DisplayDomainInspector).FullName);
                var state = inspector.Read();
                A.Eq(Path.Combine(root, "display.runtime.config"), state[0], "configuration is local to the app data");
                A.Eq(".NETFramework,Version=v4.8", state[1], "modern Framework layout behavior");
                A.Eq("True", state[2], "UI entry does not recursively bootstrap");
                A.False(DpiStartup.Configured, "caller domain is unchanged");
            }
            finally { AppDomain.Unload(domain); }
        }

        [Test("90-1,DPI")]
        public static void MissingOrBrokenDisplayConfigurationIsRepairedWithoutTouchingPreferences()
        {
            var root = Ctx.NewDir("dpi-config");
            var preferences = Path.Combine(root, "settings.json"); File.WriteAllText(preferences, "keep");
            var path = DpiStartup.PrepareConfiguration(root);
            File.WriteAllText(path, "broken");
            A.Eq(path, DpiStartup.PrepareConfiguration(root), "same owned configuration repaired");
            A.True(File.ReadAllText(path).Contains("PerMonitorV2"), "native display DPI enabled");
            A.Eq("keep", File.ReadAllText(preferences), "user preferences are untouched");
            foreach (int dpi in new[] { 96, 120, 144, 192 })
                A.Eq(510, DisplayScale.Logical(DisplayScale.Pixels(510, dpi), dpi), "saved sizes remain logical at " + dpi);
        }

        [Test("90-1,DPI")]
        public static void DisplayConfigurationWriteFailureStopsBeforeUiStartup()
        {
            var root = Ctx.NewDir("dpi-denied");
            var previous = AtomicFile.FailureInjector;
            try
            {
                AtomicFile.FailureInjector = p => new IOException("display configuration denied");
                A.Throws<IOException>(() => DpiStartup.PrepareConfiguration(root), "no silent return to blurry rendering");
            }
            finally { AtomicFile.FailureInjector = previous; }
        }
    }
}
