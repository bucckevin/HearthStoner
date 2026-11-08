using System;
using System.Linq;
using System.Windows;

namespace HearthStoner
{
    /// <summary>
    /// Saves the main window's place, size and maximized state when it closes, and puts it back when it opens again
    /// (Settings, "Remember the window's size and place"). A saved place that no screen shows any more (a monitor was
    /// unplugged) is ignored, so the window never opens out of reach.
    /// </summary>
    internal static class WindowMemory
    {
        /// <summary>How much of the window's top strip must be on a screen to reuse the saved place.</summary>
        private const double MinVisibleWidth = 200, TitleStrip = 40;

        /// <summary>Applies the saved place to <paramref name="w"/> before it's shown. Does nothing when there's none or it's off screen.</summary>
        public static void Restore(Window w, Settings s)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (s == null || !s.RememberWindow || s.WindowWidth < w.MinWidth || s.WindowHeight < w.MinHeight) return;
            var saved = new Rect(s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight);
            if (!OnSomeScreen(w, saved)) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = saved.Left;
            w.Top = saved.Top;
            w.Width = saved.Width;
            w.Height = saved.Height;
            if (s.WindowMaximized) w.WindowState = WindowState.Maximized;   // on the screen that holds Left/Top
        }

        /// <summary>Saves <paramref name="w"/>'s place (its normal-size place when maximized) to the settings file.</summary>
        public static void Save(Window w, Settings s)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            if (s == null || !s.RememberWindow) return;
            var r = w.WindowState == WindowState.Normal ? new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight) : w.RestoreBounds;
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || double.IsNaN(r.Left)) return;
            s.WindowLeft = r.Left;
            s.WindowTop = r.Top;
            s.WindowWidth = r.Width;
            s.WindowHeight = r.Height;
            s.WindowMaximized = w.WindowState == WindowState.Maximized;
            s.Save();
        }

        /// <summary>Whether enough of <paramref name="r"/>'s top strip (where you drag the window) is on a connected screen.</summary>
        private static bool OnSomeScreen(Window w, Rect r)
        {
            // Screens report device pixels; the window's place is in WPF units (1/96 inch).
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            var strip = new Rect(r.Left * dpi.DpiScaleX, r.Top * dpi.DpiScaleY, r.Width * dpi.DpiScaleX, TitleStrip * dpi.DpiScaleY);
            return System.Windows.Forms.Screen.AllScreens.Any(sc =>
            {
                var a = sc.WorkingArea;
                var visible = Rect.Intersect(strip, new Rect(a.Left, a.Top, a.Width, a.Height));
                return !visible.IsEmpty && visible.Width >= MinVisibleWidth * dpi.DpiScaleX;
            });
        }
    }
}
