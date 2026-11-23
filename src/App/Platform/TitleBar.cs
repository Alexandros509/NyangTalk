using System;
using System.Runtime.InteropServices;
using NyangTalk.Domain.People;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App.Platform
{
    /// <summary>Operating-system window attributes; replaceable in checks (the calls are recorded there).</summary>
    public interface IDwm
    {
        int OsBuild { get; }
        /// <summary>Returns 0 (S_OK) on success, anything else on failure.</summary>
        int Set(IntPtr hwnd, int attribute, int value);
    }

    public sealed class RealDwm : IDwm
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct OSVERSIONINFOEX { public int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Csd; public ushort SpMajor, SpMinor, Suite; public byte Type, Reserved; }
        [DllImport("ntdll.dll")] private static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

        private int _build = -1;

        /// <summary>Real build number (Environment.OSVersion is capped without an app manifest).</summary>
        public int OsBuild
        {
            get
            {
                if (_build >= 0) return _build;
                try
                {
                    var v = new OSVERSIONINFOEX { Size = Marshal.SizeOf(typeof(OSVERSIONINFOEX)) };
                    _build = RtlGetVersion(ref v) == 0 ? v.Build : 0;
                }
                catch (DllNotFoundException) { _build = 0; }
                catch (EntryPointNotFoundException) { _build = 0; }
                return _build;
            }
        }

        public int Set(IntPtr hwnd, int attribute, int value)
        {
            try { return DwmSetWindowAttribute(hwnd, attribute, ref value, 4); }
            catch (DllNotFoundException) { return -1; }
            catch (EntryPointNotFoundException) { return -1; }
        }
    }

    /// <summary>
    /// 90-SPEC 1.3: title bar and outer line in the theme colours using only the window manager's own attributes.
    /// Attribute numbers from Microsoft's DWMWINDOWATTRIBUTE page (read 2026-11-19): DWMWA_USE_IMMERSIVE_DARK_MODE = 20 is
    /// stated there; BORDER_COLOR, CAPTION_COLOR, TEXT_COLOR follow WINDOW_CORNER_PREFERENCE = 33 in the enum listing
    /// (34, 35, 36 — derived from that order, not printed as numbers). The page states Windows 11 Build 22000 for all four.
    /// Any failure leaves the default title bar; the title bar is never drawn by the app.
    /// </summary>
    public static class TitleBar
    {
        public const int DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;
        public const int ColorNone = unchecked((int)0xFFFFFFFE);     // DWMWA_COLOR_NONE: no border
        public const int Windows11 = 22000;

        public static int ColorRef(Rgb c) { return c.R | (c.G << 8) | (c.B << 16); }

        /// <summary>Title bar colours. Returns true when the operating system also draws the border colour (Windows 11).</summary>
        public static bool Apply(IDwm dwm, IntPtr hwnd, Theme th, Rgb? border)
        {
            if (dwm == null || hwnd == IntPtr.Zero) return false;
            try
            {
                if (dwm.OsBuild >= Windows11)
                {
                    dwm.Set(hwnd, DarkMode, th.Dark ? 1 : 0);   // light caption buttons on a dark caption
                    dwm.Set(hwnd, CaptionColor, ColorRef(th.Panel));
                    dwm.Set(hwnd, TextColor, ColorRef(th.Text));
                    return dwm.Set(hwnd, BorderColor, border.HasValue ? ColorRef(border.Value) : ColorNone) == 0;
                }
                // Windows 10: colours cannot be set; a dark theme gets the dark title bar, a light theme keeps the default
                if (ColorMath.Luminance(th.Panel) < 0.5) dwm.Set(hwnd, DarkMode, 1);
                return false;
            }
            catch (Exception) { return false; } // never break the window over a title bar colour
        }
    }
}
