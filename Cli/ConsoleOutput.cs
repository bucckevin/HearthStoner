using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HearthStoner
{
    internal static partial class Program
    {
        private const long MaxLogBytes = 5_000_000;
        private const int SwHide = 0;
        private static readonly object ConsoleLock = new object();

        /// <summary>Loads the meta stats for a bracket and period (defaults: all ranks, past 7 days) and says what was loaded.</summary>
        private static MetaStats LoadMeta(string rank, string period)
        {
            var meta = MetaStats.Load("standard", rank ?? "all", period ?? "past-7", Log);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(meta.Archetypes.Count > 0
                ? $"Meta stats: {meta.Archetypes.Count} archetypes from {meta.TotalGames:N0} games ({meta.Rank}, {meta.Period}) - Firestone"
                : "Meta stats unavailable: deck prediction and mulligan data are off.");
            Console.ResetColor();
            return meta;
        }

        /// <summary>A line that needs your attention, in yellow.</summary>
        private static void Warn(string msg) => WriteLine(msg, ConsoleColor.Yellow);

        /// <summary>A status line about the game (your deck was found ...), in cyan.</summary>
        private static void Info(string msg) => WriteLine(msg, ConsoleColor.Cyan);

        /// <summary>Diagnostics: shown with --verbose; meta download problems always.</summary>
        private static void Log(string msg)
        {
            if (msg == null || (!_verbose && !msg.StartsWith("[meta]"))) return;
            lock (ConsoleLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Error.WriteLine(msg);
                Console.ResetColor();
            }
        }

        private static void WriteLine(string msg, ConsoleColor color)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(msg);
                Console.ResetColor();
            }
        }

        /// <summary>Prints advice sections with each line coloured by its tone.</summary>
        private static void PrintSnapshot(AdviceSnapshot snap)
        {
            if (snap == null) return;
            lock (ConsoleLock)
            {
                Console.WriteLine();
                foreach (var sec in snap.Sections)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  [{sec.Title}]");
                    foreach (var l in sec.Lines)
                    {
                        Console.ForegroundColor = ColorOf(l.Tone);
                        Console.WriteLine("    " + l.Text);
                    }
                }
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        private static ConsoleColor ColorOf(Tone tone) => tone switch
        {
            Tone.Good => ConsoleColor.Green,
            Tone.Bad => ConsoleColor.Red,
            Tone.Warn => ConsoleColor.DarkYellow,
            Tone.Dim => ConsoleColor.DarkGray,
            Tone.Heading => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray,
        };

        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// App mode: hide the console window and send console output to %LocalAppData%\HearthStoner\hearthstoner.log
        /// (started fresh once it passes 5 MB). If the log can't be opened, output stays on the hidden console.
        /// </summary>
        private static void ConsoleToLogFile()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var path = AppPaths.Data("hearthstoner.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes) File.Delete(path);
                var w = TextWriter.Synchronized(new StreamWriter(path, append: true) { AutoFlush = true });
                w.WriteLine($"==== HearthStoner started {DateTime.Now:G} ====");
                Console.SetOut(w);
                Console.SetError(w);
            }
            catch (IOException) { }                    // e.g. another copy has the log open: no log file this run
            catch (UnauthorizedAccessException) { }    // no write access to the data folder: same
            ShowWindow(GetConsoleWindow(), SwHide);
        }
    }
}
