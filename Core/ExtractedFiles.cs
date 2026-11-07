#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HearthStoner
{
    /// <summary>
    /// The self-contained exe unpacks WPF's native DLLs (Windows can only load those from files) into
    /// %TEMP%\.net\HearthStoner\&lt;id&gt;\, a new folder for every build, and .NET never removes old ones. This finds
    /// the folders that belong to older builds so the installer can delete them.
    /// </summary>
    public static class ExtractedFiles
    {
        /// <summary>Where .NET unpacks this app's files: under DOTNET_BUNDLE_EXTRACT_BASE_DIR if set, else %TEMP%\.net.</summary>
        /// <param name="appName">The exe's name without ".exe".</param>
        public static string AppFolder(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("The app name is required.", nameof(appName));
            var custom = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
            var root = string.IsNullOrWhiteSpace(custom) ? Path.Combine(Path.GetTempPath(), ".net") : custom;
            return Path.Combine(root, appName);
        }

        /// <summary>The folders this process loads native DLLs from (the runtime lists its unpack folder there).</summary>
        public static IReadOnlyList<string> InUse() =>
            ((AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        /// <summary>
        /// The unpack folders of older builds: every folder in <paramref name="appFolder"/> except those in use.
        /// Returns nothing unless one of <paramref name="inUse"/> is inside <paramref name="appFolder"/>, so a run that
        /// can't tell which folder is its own never deletes anything.
        /// </summary>
        /// <param name="appFolder">From <see cref="AppFolder"/>.</param>
        /// <param name="inUse">From <see cref="InUse"/>.</param>
        public static IReadOnlyList<string> Stale(string appFolder, IEnumerable<string> inUse)
        {
            if (string.IsNullOrEmpty(appFolder) || !Directory.Exists(appFolder)) return Array.Empty<string>();
            var root = Normalize(appFolder);
            var used = (inUse ?? Enumerable.Empty<string>()).Select(Normalize).Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!used.Any(p => p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return Array.Empty<string>();
            return Directory.GetDirectories(appFolder).Where(d => !used.Contains(Normalize(d))).ToList();
        }

        /// <summary>
        /// Deletes the folders <see cref="Stale"/> finds. A folder with any file still open (another running copy has
        /// its DLLs loaded) is skipped whole, so no running copy ever loses part of its files.
        /// </summary>
        /// <returns>How many folders were deleted.</returns>
        public static int RemoveStale(string appFolder, IEnumerable<string> inUse)
        {
            var removed = 0;
            foreach (var dir in Stale(appFolder, inUse))
            {
                if (AnyFileOpen(dir)) continue;
                try { Directory.Delete(dir, recursive: true); removed++; }
                catch (IOException ex) { Diag.Ignored(ex, "ExtractedFiles"); }                  // a running copy has its DLLs loaded
                catch (UnauthorizedAccessException ex) { Diag.Ignored(ex, "ExtractedFiles"); }
            }
            return removed;
        }

        /// <summary>True if some file in the folder can't be opened exclusively (a loaded DLL can't).</summary>
        private static bool AnyFileOpen(string dir)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                    catch (IOException) { return true; }
                    catch (UnauthorizedAccessException) { return true; }
                }
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Diag.Ignored(ex, "ExtractedFiles");
                return true;   // can't even look: leave it alone
            }
        }

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return ""; }
        }
    }
}
