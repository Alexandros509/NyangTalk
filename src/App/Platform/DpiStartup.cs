using System;
using System.IO;
using NyangTalk.Adapters.Storage;

namespace NyangTalk.App.Platform
{
    /// <summary>
    /// .NET Framework reads WinForms' DPI options from the application domain's configuration.
    /// Keep the downloadable EXE self-contained: the UI domain uses an owned config in the data directory.
    /// The watcher and connector helper never enter this domain or initialize a window.
    /// </summary>
    public static class DpiStartup
    {
        private const string DomainKey = "NyangTalk.DisplayConfigured";
        public const string Configuration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<configuration><startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup>"
            + "<System.Windows.Forms.ApplicationConfigurationSection>"
            + "<add key=\"DpiAwareness\" value=\"PerMonitorV2\"/>"
            + "</System.Windows.Forms.ApplicationConfigurationSection></configuration>\n";

        public static bool Configured { get { return true.Equals(AppDomain.CurrentDomain.GetData(DomainKey)); } }

        public static string PrepareConfiguration(string dataRoot)
        {
            var path = Path.Combine(dataRoot, "display.runtime.config");
            if (!File.Exists(path) || File.ReadAllText(path) != Configuration)
                AtomicFile.WriteAllText(path, Configuration);
            return path;
        }

        public static AppDomain CreateUiDomain(string executable, string dataRoot)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = Path.GetDirectoryName(Path.GetFullPath(executable)),
                ConfigurationFile = PrepareConfiguration(dataRoot),
                TargetFrameworkName = ".NETFramework,Version=v4.8"
            };
            var domain = AppDomain.CreateDomain("NyangTalk UI", null, setup);
            domain.SetData(DomainKey, true);
            return domain;
        }

        public static int Run(string[] args, string dataRoot)
        {
            var executable = typeof(DpiStartup).Assembly.Location;
            var domain = CreateUiDomain(executable, dataRoot);
            try { return domain.ExecuteAssembly(executable, args); }
            finally { AppDomain.Unload(domain); }
        }
    }
}
