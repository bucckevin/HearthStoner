using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HearthStoner
{
    /// <summary>
    /// Starting Hearthstone, and bringing it to the front once it runs. The reliable way to start it is through the
    /// Battle.net app: <c>Battle.net.exe --exec="launch WTCG"</c> (WTCG is Hearthstone's Battle.net code) signs in,
    /// applies any update and starts the game. Battle.net is found from your setting, then the registry, then the
    /// default install folder. Running Hearthstone.exe yourself also works, but it opens Battle.net first.
    /// </summary>
    public static class GameLauncher
    {
        public const string DefaultBattleNet = @"C:\Program Files (x86)\Battle.net\Battle.net.exe";

        public sealed class Target
        {
            /// <summary>The program to run, or null if nothing was found.</summary>
            public string Path;
            public string Arguments;
            /// <summary>Where it came from, for the Settings page ("your setting", "the registry", ...).</summary>
            public string Source;
            public bool IsBattleNet => FileName(Path).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRunning => Process.GetProcessesByName("Hearthstone").Length > 0;

        /// <summary>What a launch would run. <paramref name="registry"/> reads (key, value) from HKLM/HKCR; replaceable for tests.</summary>
        public static Target Resolve(string configuredPath, Func<string, string, string> registry = null, Func<string, bool> exists = null)
        {
            registry = registry ?? ReadRegistry;
            exists = exists ?? File.Exists;
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var p = configuredPath.Trim().Trim('"');
                if (exists(p)) return For(p, "your setting");
            }
            foreach (var (key, value, source) in new[]
            {
                (@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", "DisplayIcon", "the Battle.net install"),
                (@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Battle.net", "InstallLocation", "the Battle.net install"),
                (@"HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net\Capabilities", "ApplicationIcon", "the Battle.net app registration"),
                (@"HKCR\battlenet\shell\open\command", "", "the battlenet:// link handler"),
            })
            {
                var exe = ExeFrom(registry(key, value));
                if (exe != null && exists(exe)) return For(exe, source);
            }
            if (exists(DefaultBattleNet)) return For(DefaultBattleNet, "the default install folder");
            return new Target();
        }

        private static Target For(string path, string source) => new Target
        {
            Path = path,
            Source = source,
            Arguments = FileName(path).Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) ? "--exec=\"launch WTCG\"" : "",
        };

        /// <summary>The file name of a Windows path (works the same when tests run on Linux).</summary>
        public static string FileName(string path) =>
            string.IsNullOrEmpty(path) ? "" : path.Substring(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1);

        /// <summary>The Battle.net.exe in a registry value: a path, a folder, "path",0 or "path" "%1".</summary>
        public static string ExeFrom(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var v = value.Trim();
            if (v.StartsWith("\"")) { var end = v.IndexOf('"', 1); if (end > 0) v = v.Substring(1, end - 1); }
            else v = System.Text.RegularExpressions.Regex.Replace(v, @",-?\d+$", "");   // icon index: path,0
            if (!v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = v.TrimEnd('\\', '/') + "\\Battle.net.exe";
            return v;
        }

        /// <summary>Starts Hearthstone. Returns null on success, or what went wrong.</summary>
        public static string Launch(string configuredPath)
        {
            var t = Resolve(configuredPath);
            try
            {
                if (t.Path != null)
                {
                    Process.Start(new ProcessStartInfo(t.Path, t.Arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(t.Path) });
                    return null;
                }
                // Last resort: the battlenet:// link opens the Battle.net app at Hearthstone, if it's installed at all.
                Process.Start(new ProcessStartInfo("battlenet://WTCG") { UseShellExecute = true });
                return null;
            }
            catch (Exception ex)
            {
                return t.Path == null
                    ? "Couldn't find Battle.net. Choose Battle.net.exe or Hearthstone.exe in Settings."
                    : $"Couldn't start {FileName(t.Path)}: {ex.Message}";
            }
        }

        /// <summary>Brings the Hearthstone window to the front (restoring it if minimised). False if it isn't running.</summary>
        public static bool BringToFront()
        {
            var p = Process.GetProcessesByName("Hearthstone").FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
            if (p == null) return false;
            var h = p.MainWindowHandle;
            if (IsIconic(h)) ShowWindow(h, 9);   // SW_RESTORE
            return SetForegroundWindow(h);
        }

        private static string ReadRegistry(string key, string value)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                var root = key.StartsWith(@"HKCR\") ? Microsoft.Win32.Registry.ClassesRoot : Microsoft.Win32.Registry.LocalMachine;
                using (var k = root.OpenSubKey(key.Substring(5)))
                    return k?.GetValue(value) as string;
            }
            catch { return null; }
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    }
}
