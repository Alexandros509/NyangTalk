using System.Drawing;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NyangTalk.App.Platform
{
    /// <summary>Taskbar flashing (FlashWindowEx) and the unread badge (ITaskbarList3 overlay icon), 70-SPEC 1장.</summary>
    public static class Taskbar
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

        [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
        private const uint FLASHW_STOP = 0, FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

        public static void Flash(IntPtr hwnd, bool on)
        {
            var fi = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)), hwnd = hwnd, dwFlags = on ? FLASHW_ALL | FLASHW_TIMERNOFG : FLASHW_STOP, uCount = uint.MaxValue, dwTimeout = 0 };
            try { FlashWindowEx(ref fi); } catch (EntryPointNotFoundException) { }
        }

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit(); void AddTab(IntPtr hwnd); void DeleteTab(IntPtr hwnd); void ActivateTab(IntPtr hwnd); void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
            void SetProgressValue(IntPtr hwnd, ulong done, ulong total); void SetProgressState(IntPtr hwnd, int flags);
            void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI); void UnregisterTab(IntPtr hwndTab); void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
            void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint reserved); void ThumbBarAddButtons(IntPtr hwnd, uint c, IntPtr b);
            void ThumbBarUpdateButtons(IntPtr hwnd, uint c, IntPtr b); void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
            void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        private class TaskbarListClass { }

        private static ITaskbarList3 _list;
        private static int _lastCount = -1;
        private static IntPtr _lastWindow;

        public static void SetBadge(IntPtr hwnd, int count)
        {
            if (hwnd == _lastWindow && count == _lastCount) return;
            try
            {
                if (_list == null) { _list = (ITaskbarList3)new TaskbarListClass(); _list.HrInit(); }
                if (count <= 0) { _list.SetOverlayIcon(hwnd, IntPtr.Zero, null); _lastWindow = hwnd; _lastCount = count; return; }
                using (var bmp = new Bitmap(16, 16))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.FillEllipse(Brushes.Firebrick, 0, 0, 15, 15);
                        var t = count > 99 ? "99" : count.ToString();
                        using (var f = new Font("Segoe UI", t.Length > 1 ? 6.5f : 8f, FontStyle.Bold, GraphicsUnit.Point))
                            TextRenderer.DrawText(g, t, f, new Rectangle(0, 0, 16, 16), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                    IntPtr h = bmp.GetHicon();
                    try { _list.SetOverlayIcon(hwnd, h, count.ToString()); }
                    finally { DestroyIcon(h); }
                }
                _lastWindow = hwnd; _lastCount = count;
            }
            // The taskbar may be missing or restarting (Explorer restart, locked-down session): the badge is then skipped.
            // This runs inside window activation, where an exception would end the whole app, so every COM failure
            // (COMException, E_NOTIMPL → NotImplementedException, E_ACCESSDENIED → UnauthorizedAccessException, …) stops here.
            catch (Exception ex)
            {
                if (!(ex is ExternalException || ex is InvalidCastException || ex is NotImplementedException || ex is UnauthorizedAccessException || ex is ArgumentException)) throw;
                _list = null;   // created but not initialised: try a fresh one next time
                _lastWindow = IntPtr.Zero; _lastCount = -1;
            }
        }
    }
}
