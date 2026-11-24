using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NyangTalk.Adapters.GameLink
{
    public enum ShortcutPlace { StartMenu, Desktop }

    /// <summary>User-selected Windows shell links, always targeting the app's fixed launch path.</summary>
    public static class Shortcuts
    {
        public static string Folder(ShortcutPlace place)
        {
            return Environment.GetFolderPath(place == ShortcutPlace.StartMenu ? Environment.SpecialFolder.Programs : Environment.SpecialFolder.DesktopDirectory);
        }

        public static string Create(string dataRoot, string selfExe, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new IOException("Shortcut folder is unavailable");
            string why;
            var target = FixedLaunchPath.Ensure(dataRoot, selfExe, out why);
            if (target == null) throw new IOException(why);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "NyangTalk.lnk");
            object shell = null, link = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                if (File.Exists(path))
                {
                    var existing = (string)Get(link, "TargetPath");
                    if (!string.Equals(Path.GetFullPath(existing), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("A shortcut with this name already points to another program");
                }
                Set(link, "TargetPath", target);
                Set(link, "Arguments", "");
                Set(link, "WorkingDirectory", Path.GetDirectoryName(target));
                Set(link, "IconLocation", target + ",0");
                Set(link, "Description", "NyangTalk");
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                if (!File.Exists(path)) throw new IOException("Shortcut was not created");
                return path;
            }
            catch (TargetInvocationException ex) { throw new IOException("Windows could not create the shortcut", ex.InnerException ?? ex); }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static object Get(object target, string name) { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); }
        private static void Set(object target, string name, object value) { target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }); }
    }
}
