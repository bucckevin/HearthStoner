#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HearthStoner
{
    /// <summary>
    /// The app's side of updating itself (<see cref="AppUpdate"/> does the GitHub work): a check once a day while
    /// "Check for updates" is on, what was found for the Settings page and the tray, and installing. Installing
    /// downloads and verifies the new exe, then its own 'install' command closes this copy (after a game in
    /// progress ends), replaces it and starts the new one.
    /// </summary>
    public static class AppUpdater
    {
        private static readonly object Gate = new object();
        private static AppRelease? _available;
        private static string _status = "Not checked yet.";
        private static bool _busy;
        private static Timer? _daily;

        /// <summary>Raised (on a background thread) whenever <see cref="Available"/>, <see cref="Status"/> or <see cref="Busy"/> change.</summary>
        public static event Action? Changed;

        /// <summary>A release to show as available instead of the real state (screenshots only).</summary>
        public static AppRelease? Override { get; set; }

        /// <summary>A newer release, or null.</summary>
        public static AppRelease? Available { get { lock (Gate) return Override ?? _available; } }

        /// <summary>One line for the Settings page: up to date, available, downloading, or what went wrong.</summary>
        public static string Status { get { lock (Gate) return Override != null ? $"HearthStoner {Override.Version} is available." : _status; } }

        /// <summary>A check or install is running.</summary>
        public static bool Busy { get { lock (Gate) return _busy; } }

        /// <summary>Checks a minute after starting, then every 24 hours, whenever <paramref name="enabled"/> says so.</summary>
        /// <param name="enabled">Read before each check (the "Check for updates" setting).</param>
        public static void StartDailyChecks(Func<bool> enabled)
        {
            if (enabled == null) throw new ArgumentNullException(nameof(enabled));
            _daily?.Dispose();
            _daily = new Timer(_ => { if (enabled()) CheckNow(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
        }

        /// <summary>Asks GitHub for the latest release in the background (does nothing if already busy).</summary>
        public static void CheckNow()
        {
            if (!Begin("Checking...")) return;
            Task.Run(() =>
            {
                var result = AppUpdate.Check(AppVersion.Current.Number);
                lock (Gate) _available = result.Newer;
                End(result.Error
                    ?? (result.Newer != null ? $"HearthStoner {result.Newer.Version} is available." : $"Up to date (checked {DateTime.Now:h:mm tt})."));
            });
        }

        /// <summary>Downloads the available release in the background and hands over to its installer.</summary>
        public static void Install()
        {
            var release = Available;
            if (release == null || Override != null || !Begin($"Downloading {release.Version}...")) return;
            Task.Run(() =>
            {
                try
                {
                    var exe = AppUpdate.Download(release, Path.Combine(Path.GetTempPath(), "HearthStoner-update"));
                    Process.Start(new ProcessStartInfo(exe, "install")
                    {
                        WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false, CreateNoWindow = true,
                    });
                    End(HearthstoneLocator.GameInProgress(HearthstoneLocator.FindInstallDir())
                        ? $"{release.Version} is ready: it installs when your game ends."
                        : $"Installing {release.Version}: HearthStoner restarts in a moment.");
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException
                                               or IOException or System.ComponentModel.Win32Exception)
                {
                    End("The update didn't install: " + ex.Message);
                }
            });
        }

        private static bool Begin(string status)
        {
            lock (Gate)
            {
                if (_busy) return false;
                _busy = true;
                _status = status;
            }
            Changed?.Invoke();
            return true;
        }

        private static void End(string status)
        {
            lock (Gate)
            {
                _busy = false;
                _status = status;
            }
            Changed?.Invoke();
        }
    }
}
