using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace HearthStoner
{
    /// <summary>Start with Windows: a value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    public static class Autostart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "HearthStoner";

        private static string Command => $"\"{Process.GetCurrentProcess().MainModule?.FileName}\" app --minimized";

        public static bool IsEnabled
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k?.GetValue(Name) != null; }
                catch { return false; }
            }
        }

        /// <summary>If start-with-Windows is on but points at another exe (e.g. the old .NET Framework build), point it here.</summary>
        public static void Refresh()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key, writable: true))
                {
                    var cur = k?.GetValue(Name) as string;
                    if (cur != null && cur != Command) k.SetValue(Name, Command);
                }
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "AppHost"); }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key, writable: true) ?? Registry.CurrentUser.CreateSubKey(Key))
                {
                    if (on) k.SetValue(Name, Command);
                    else if (k.GetValue(Name) != null) k.DeleteValue(Name);
                }
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "AppHost"); }
        }
    }

    /// <summary>
    /// Runs the overlay, the app window and the tray icon on one WPF UI thread (WPF allows one Application per
    /// process). The game tracking keeps running on the caller's thread.
    /// </summary>
    public static class AppHost
    {
        public static Thread Start(Func<AdviceSnapshot> overlaySource, Advisor advisor, Settings settings, Action<string> log, AppServices services, bool showWindow)
        {
            Autostart.Refresh();
            var t = new Thread(() =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var overlay = new OverlayWindow(overlaySource, advisor, settings, log);
                overlay.Show();

                MainWindow window = null;
                void ShowWindow()
                {
                    if (window == null)
                    {
                        window = new MainWindow(services);
                        window.Closed += (s, e) => window = null;   // closing the window keeps HearthStoner in the tray
                    }
                    window.Show();
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                }

                var tray = new System.Windows.Forms.NotifyIcon
                {
                    Text = "HearthStoner " + AppVersion.Current.Number,
                    Icon = AppIcon(),
                    Visible = true,
                };
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("HearthStoner " + AppVersion.Current.Number) { Enabled = false, ToolTipText = AppVersion.Current.Detail });
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Open HearthStoner", null, (s, e) => app.Dispatcher.Invoke(ShowWindow));
                var overlayItem = new System.Windows.Forms.ToolStripMenuItem("Overlay") { Checked = true, CheckOnClick = true };
                overlayItem.CheckedChanged += (s, e) => app.Dispatcher.Invoke(() => overlay.UserHidden = !overlayItem.Checked);
                menu.Items.Add(overlayItem);
                var startItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled, CheckOnClick = true };
                startItem.CheckedChanged += (s, e) => { Autostart.Set(startItem.Checked); settings.StartWithWindows = startItem.Checked; settings.Save(); };
                menu.Items.Add(startItem);
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add("Quit", null, (s, e) => { tray.Visible = false; tray.Dispose(); Environment.Exit(0); });
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += (s, e) => app.Dispatcher.Invoke(ShowWindow);

                if (showWindow) ShowWindow();
                app.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Name = "HearthStoner UI";
            t.Start();
            return t;
        }

        /// <summary>The app icon at the tray's size (the .ico has hand-tuned 16-32 px versions).</summary>
        private static System.Drawing.Icon AppIcon()
        {
            try
            {
                var res = Application.GetResourceStream(new Uri("pack://application:,,,/HearthStoner;component/App/hearthstoner.ico"));
                if (res != null) using (res.Stream) return new System.Drawing.Icon(res.Stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "AppHost"); }
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (exe != null) return System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? System.Drawing.SystemIcons.Application;
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "AppHost"); }
            return System.Drawing.SystemIcons.Application;
        }
    }
}
