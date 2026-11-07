#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace HearthStoner
{
    internal static partial class Program
    {
        /// <summary>'update [--check]': installs the latest GitHub release if it's newer (--check: only says).</summary>
        /// <param name="checkOnly">Report what's available without downloading.</param>
        private static int UpdateCommand(bool checkOnly)
        {
            var current = AppVersion.Current.Number;
            var status = AppUpdate.Check(current);
            if (status.Error != null) { Console.Error.WriteLine(status.Error); return 1; }
            if (status.Newer == null) { Console.WriteLine($"HearthStoner {current} is the latest version."); return 0; }
            Console.WriteLine($"HearthStoner {status.Newer.Version} is available (you have {current}): {status.Newer.PageUrl}");
            if (checkOnly) return 0;

            string exe;
            try
            {
                Console.WriteLine("Downloading...");
                exe = AppUpdate.Download(status.Newer, Path.Combine(Path.GetTempPath(), "HearthStoner-update"));
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or IOException)
            {
                Console.Error.WriteLine("The update didn't download: " + ex.Message);
                return 1;
            }
            // The new exe's own 'install' closes this copy, waits for a game in progress to end, and replaces it.
            Process.Start(new ProcessStartInfo(exe, "install") { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false });
            Console.WriteLine("Installing the update.");
            return 0;
        }
    }
}
