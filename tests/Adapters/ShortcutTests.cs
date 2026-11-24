using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NyangTalk.Adapters.GameLink;

namespace NyangTalk.Tests
{
    public static class ShortcutTests
    {
        [Test("shortcuts,windows-shell")]
        public static void RealShellLinksUseTheFixedExecutableAndOriginalIconWithoutWatcherArguments()
        {
            var root = Ctx.NewDir("shell-shortcut");
            var data = Path.Combine(root, "app data 한글");
            var folder = Path.Combine(root, "selected destination");
            var self = typeof(ShortcutTests).Assembly.Location;
            var path = Shortcuts.Create(data, self, folder);
            A.True(File.Exists(path), "a real lnk file was written in an isolated test directory");
            object shell = null, link = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Func<string, string> get = name => (string)link.GetType().InvokeMember(name, BindingFlags.GetProperty, null, link, null);
                var target = FixedLaunchPath.For(data);
                A.Eq(Path.GetFullPath(target), get("TargetPath"), "points at the stable app path including Unicode and spaces");
                A.Eq("", get("Arguments"), "opens the messenger, not the game watcher");
                A.Eq(target + ",0", get("IconLocation").Replace(", ", ","), "uses the executable's embedded original icon");
                A.Eq(Path.GetFullPath(data), get("WorkingDirectory"), "working directory is stable");
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); if (shell != null) Marshal.FinalReleaseComObject(shell); }
            A.Eq(path, Shortcuts.Create(data, self, folder), "creating the same shortcut twice is safe");
            var other = Path.Combine(root, "foreign target");
            A.Throws<IOException>(() => Shortcuts.Create(other, self, folder), "a link belonging to a different target is not overwritten");
            A.True(File.Exists(path), "the existing shortcut survives refusal");
        }
    }
}
