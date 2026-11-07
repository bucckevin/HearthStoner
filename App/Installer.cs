using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace HearthStoner
{
    /// <summary>
    /// 'HearthStoner install' and 'uninstall': a per-user install (no admin) to %LocalAppData%\Programs\HearthStoner,
    /// with Start menu and desktop shortcuts and an entry in Windows' installed apps. Your data (%LocalAppData%\HearthStoner)
    /// is never touched. HEARTHSTONER_INSTALL_TEST=&lt;folder&gt; moves everything somewhere harmless for testing.
    /// </summary>
    public static class Installer
    {
        private const string Name = "HearthStoner";
        private static readonly string Test = Environment.GetEnvironmentVariable("HEARTHSTONER_INSTALL_TEST");

        public static string Dir => Test ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);
        public static string Exe => Path.Combine(Dir, Name + ".exe");
        private static string StartMenu => Test != null ? Path.Combine(Test, "shortcuts") : Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        private static string Desktop => Test != null ? Path.Combine(Test, "shortcuts") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private static string UninstallKey => Test != null ? @"Software\HearthStonerTest\Uninstall" : @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Name;
        private static string RunKey => Test != null ? @"Software\HearthStonerTest\Run" : @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string Self => Environment.ProcessPath;
        public static bool IsInstalledCopy => string.Equals(Path.GetFullPath(Self), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);

        [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList(uint[] list, uint count);
        /// <summary>Started by double-click (the console is ours alone), not from a terminal.</summary>
        public static bool OwnConsole { get { try { return GetConsoleProcessList(new uint[2], 2) == 1; } catch { return false; } } }

        /// <summary>A double-clicked exe outside the install folder offers to install itself.</summary>
        public static int Offer()
        {
            var answer = System.Windows.Forms.MessageBox.Show($"Install HearthStoner for your Windows user?\n\nIt goes to {Dir}, with Start menu and desktop shortcuts. Your games and settings are kept.",
                Name, System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
            return answer == System.Windows.Forms.DialogResult.Yes ? Install(noStart: false) : 0;
        }

        public static int Install(bool noStart)
        {
            if (File.Exists(Path.Combine(Path.GetDirectoryName(Self), Name + ".dll")))
            {
                Console.Error.WriteLine("This build isn't a single exe: install from install.bat (dotnet publish), not from bin.");
                return 1;
            }
            WaitForGameToEnd();
            if (!IsInstalledCopy)
            {
                CloseRunningCopies();
                Directory.CreateDirectory(Dir);
                // The old exe can stay locked for a moment after its process ends.
                for (var i = 0; ; i++)
                {
                    try { File.Copy(Self, Exe, overwrite: true); break; }
                    catch (IOException) when (i < 20) { Thread.Sleep(500); }
                }
            }
            Directory.CreateDirectory(StartMenu);
            Directory.CreateDirectory(Desktop);
            Shortcut(Path.Combine(StartMenu, Name + ".lnk"));
            Shortcut(Path.Combine(Desktop, Name + ".lnk"));
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Name);
                k.SetValue("DisplayIcon", Exe + ",0");
                k.SetValue("DisplayVersion", Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");
                k.SetValue("Publisher", Name);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", $"\"{Exe}\" uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            }
            Console.WriteLine($"Installed to {Dir}. HearthStoner is in your Start menu and on your desktop.");
            if (!noStart) Process.Start(new ProcessStartInfo(Exe, "app") { WorkingDirectory = Dir, UseShellExecute = false });
            return 0;
        }

        public static int Uninstall(bool yes)
        {
            if (!yes && System.Windows.Forms.MessageBox.Show("Remove HearthStoner?\n\nYour games and settings in %LocalAppData%\\HearthStoner are kept.", Name,
                    System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes)
                return 0;
            CloseRunningCopies();
            foreach (var lnk in new[] { Path.Combine(StartMenu, Name + ".lnk"), Path.Combine(Desktop, Name + ".lnk") })
                try { File.Delete(lnk); } catch (Exception swallowed) { Diag.Ignored(swallowed, "Installer"); }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch (Exception swallowed) { Diag.Ignored(swallowed, "Installer"); }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                    if (k?.GetValue(Name) != null) k.DeleteValue(Name);
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Installer"); }
            // This exe may be the one in the folder: a cmd outside it deletes the folder once this process has ended.
            if (File.Exists(Exe))
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{Dir}\"")
                {
                    WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true, UseShellExecute = false,
                });
            Console.WriteLine("HearthStoner is uninstalled. Your data is still in %LocalAppData%\\HearthStoner.");
            if (!yes) System.Windows.Forms.MessageBox.Show("HearthStoner is uninstalled. Your games and settings are still in %LocalAppData%\\HearthStoner.", Name);
            return 0;
        }

        /// <summary>Never swap the app out from under you mid-game: wait for the game in Power.log to end.</summary>
        private static void WaitForGameToEnd()
        {
            var hs = HearthstoneLocator.FindInstallDir();
            var told = false;
            while (HearthstoneLocator.GameInProgress(hs))
            {
                if (!told) { Console.WriteLine("You're in a Hearthstone game: installing when it ends..."); told = true; }
                Thread.Sleep(15000);
            }
        }

        /// <summary>Ends the installed copy (only that exe, never this process).</summary>
        private static void CloseRunningCopies()
        {
            var me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName(Name).Where(p => p.Id != me))
            {
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception swallowed) { Diag.Ignored(swallowed, "Installer"); }
            }
        }

        private static void Shortcut(string path)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var s = shell.CreateShortcut(path);
            s.TargetPath = Exe;
            s.Arguments = "app";
            s.WorkingDirectory = Dir;
            s.IconLocation = Exe + ",0";
            s.Description = "Hearthstone tracker and coach";
            s.Save();
        }
    }
}
