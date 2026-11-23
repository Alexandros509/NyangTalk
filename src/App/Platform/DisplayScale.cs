using System;
using System.Drawing;
using System.Windows.Forms;
using NyangTalk.Domain.Preferences;

namespace NyangTalk.App.Platform
{
    public static class DisplayScale
    {
        public static int Dpi(Control control) { return control.DeviceDpi > 0 ? control.DeviceDpi : 96; }
        public static int Pixels(int logical, int dpi) { return WindowSizes.Scale(logical, dpi); }
        public static int Logical(int pixels, int dpi) { return (int)Math.Round(pixels * 96.0 / Math.Max(96, dpi)); }
        public static Size Size(int width, int height, int dpi) { return new Size(Pixels(width, dpi), Pixels(height, dpi)); }
    }
}
