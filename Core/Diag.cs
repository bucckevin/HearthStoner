#nullable enable
using System;

namespace HearthStoner
{
    /// <summary>
    /// Records errors that are caught on purpose because the program can carry on without that step (a cache that
    /// couldn't be written, a file that couldn't be deleted). Nothing is thrown or shown to you: the error is written
    /// to the diagnostics sink, which is hearthstoner.log in the app and the --verbose output on the command line.
    /// Without a sink (tests) it does nothing.
    /// </summary>
    public static class Diag
    {
        private static volatile Action<string>? _sink;

        /// <summary>Sets where caught errors are written; null turns recording off.</summary>
        /// <param name="sink">Receives one line per error. Must be safe to call from any thread.</param>
        public static void SetSink(Action<string>? sink) => _sink = sink;

        /// <summary>Records an error that was caught and deliberately not acted on.</summary>
        /// <param name="ex">The error; ignored when null.</param>
        /// <param name="context">Where it happened, e.g. "Settings" or "ArtCache"; shown in brackets.</param>
        public static void Ignored(Exception? ex, string? context)
        {
            var sink = _sink;
            if (sink == null || ex == null) return;
            try { sink($"[{context ?? "?"}] {ex.GetType().Name}: {ex.Message}"); }
            catch (Exception) { /* the sink itself failed (log file gone): there is nowhere left to report to */ }
        }
    }
}
