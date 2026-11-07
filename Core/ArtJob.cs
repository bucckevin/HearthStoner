using System;
using System.IO;
using System.Threading;

namespace HearthStoner
{
    /// <summary>Makes the replay board art from a Hearthstone install. The app's implementation reads the game's asset files.</summary>
    public interface IGameArtBuilder
    {
        /// <summary>The Hearthstone build the install holds (art is remade when it changes).</summary>
        /// <param name="installDir">The Hearthstone install folder.</param>
        /// <returns>The version text, or null when no Hearthstone is there.</returns>
        string GameVersion(string installDir);

        /// <summary>Makes the art into <paramref name="outDir"/>.</summary>
        /// <param name="installDir">The Hearthstone install folder.</param>
        /// <param name="outDir">Where the art goes; created by the caller.</param>
        /// <param name="progress">Called with (stage, done, total) as it goes.</param>
        /// <param name="cancel">Stops the build.</param>
        void Build(string installDir, string outDir, Action<string, int, int> progress, CancellationToken cancel);
    }

    /// <summary>
    /// Makes the replay board art (%LocalAppData%\HearthStoner\gameart) from your Hearthstone install on a low-priority
    /// background thread: at app start when it's missing or made for another Hearthstone version, and from Settings'
    /// Regenerate button. Waits while you're in a game. The builder is plugged in by the app (GameArt.StartJob), so
    /// this part builds in Core and is tested there.
    /// </summary>
    public static class ArtJob
    {
        public sealed class State
        {
            public string Kind = "idle";   // idle, checking, waiting, running, done, failed
            public string Stage, Version, Error;
            public int Done, Total, Boards;

            public string Text => Kind switch
            {
                "checking" => "Board art: checking...",
                "waiting" => "Board art: waiting for your game to end",
                "running" => Total > 0 ? $"Making board art: {Stage} {Done} of {Total}" : $"Making board art: {Stage}",
                "done" => $"Board art: up to date for Hearthstone {Version} ({Boards} board{(Boards == 1 ? "" : "s")})",
                "failed" => $"Couldn't make board art: {Error}",
                _ => "Board art: not checked yet",
            };
        }

        /// <summary>The job's state, replaced (never changed) as it goes, so any thread can read it.</summary>
        public static volatile State Status = new State();
        /// <summary>Counts finished builds, so a page can tell new art has arrived.</summary>
        public static int Builds;
        public static event Action Finished;

        // Set once by Configure; read by the job thread.
        private static volatile IGameArtBuilder _builder;
        private static volatile Func<bool> _inGame;

        public static string Dir => AppPaths.Data("gameart");

        private static int _busy;

        /// <summary>
        /// Plugs in the art builder (the app's GameArtBuilder; a fake in tests) and the app's own in-game flag.
        /// Call before <see cref="Start"/> or <see cref="Run"/>; calling again replaces the builder.
        /// </summary>
        /// <param name="builder">Makes the art; must not be null.</param>
        /// <param name="inGame">True while you're in a game; null keeps the flag set before (Hearthstone's log is always checked too).</param>
        public static void Configure(IGameArtBuilder builder, Func<bool> inGame)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            if (inGame != null) _inGame = inGame;
        }

        /// <summary>Art is made when there's none yet, or it was made for another version of Hearthstone.</summary>
        public static bool NeedsBuild(string madeFor, int boards, string gameVersion) =>
            boards == 0 || !string.Equals((madeFor ?? "").Trim(), gameVersion, StringComparison.Ordinal);

        /// <summary>Starts the job unless it's already running. <paramref name="force"/> rebuilds even when up to date.</summary>
        public static void Start(string installDir, bool force = false)
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            var t = new Thread(() => { try { Run(installDir, Dir, force); } finally { _busy = 0; } })
            {
                IsBackground = true, Priority = ThreadPriority.Lowest, Name = "board art",
            };
            t.Start();
        }

        /// <summary>The job itself, on the calling thread (Start's thread, or a test's).</summary>
        public static void Run(string installDir, string outDir, bool force, int pollMs = 30000)
        {
            try
            {
                Status = new State { Kind = "checking" };
                var builder = _builder ?? throw new InvalidOperationException("the board art builder isn't set up (ArtJob.Configure wasn't called).");
                var version = installDir == null ? null : builder.GameVersion(installDir);
                if (string.IsNullOrEmpty(version)) throw new Exception("Hearthstone wasn't found.");
                if (!force && !NeedsBuild(MadeFor(outDir), CountBoards(outDir), version))
                {
                    Status = new State { Kind = "done", Version = version, Boards = CountBoards(outDir) };
                    return;
                }

                // Not while you play: wait before starting, and pause between steps if a game starts.
                var nextCheck = DateTime.MinValue;
                void WaitForGame(Func<State> resume)
                {
                    if (DateTime.UtcNow < nextCheck) return;
                    if (InGameNow(installDir))
                    {
                        Status = new State { Kind = "waiting" };
                        while (InGameNow(installDir)) Thread.Sleep(pollMs);
                        Status = resume();
                    }
                    nextCheck = DateTime.UtcNow.AddSeconds(10);
                }
                var starting = new State { Kind = "running", Stage = "starting" };
                WaitForGame(() => starting);
                Status = starting;
                Directory.CreateDirectory(outDir);
                builder.Build(installDir, outDir, (stage, done, total) =>
                {
                    var now = new State { Kind = "running", Stage = stage, Done = done, Total = total };
                    Status = now;
                    WaitForGame(() => now);
                }, CancellationToken.None);
                File.WriteAllText(Path.Combine(outDir, "version.txt"), version);
                Status = new State { Kind = "done", Version = version, Boards = CountBoards(outDir) };
                Interlocked.Increment(ref Builds);
                Finished?.Invoke();
            }
            catch (Exception ex)
            {
                Status = new State { Kind = "failed", Error = ex.Message };
            }
        }

        private static bool InGameNow(string installDir) => (_inGame?.Invoke() ?? false) || HearthstoneLocator.GameInProgress(installDir);

        private static string MadeFor(string outDir)
        {
            var f = Path.Combine(outDir, "version.txt");
            return File.Exists(f) ? File.ReadAllText(f) : null;
        }

        private static int CountBoards(string outDir)
        {
            var d = Path.Combine(outDir, "boards");
            return Directory.Exists(d) ? Directory.GetFiles(d, "*.jpg").Length : 0;
        }
    }
}
