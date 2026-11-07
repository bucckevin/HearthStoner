#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace HearthStoner.Live
{
    /// <summary>Read access to the running Hearthstone client that survives the game being closed and reopened.</summary>
    public interface ILiveMemory : IDisposable
    {
        /// <summary>
        /// Runs <paramref name="read"/> against the current attachment, attaching (again) first if needed.
        /// </summary>
        /// <typeparam name="T">What the read returns.</typeparam>
        /// <param name="read">The memory read. An exception thrown by it drops the attachment and the read is tried once more.</param>
        /// <returns>The read's result, or <c>default</c> when Hearthstone isn't running or both attempts failed.</returns>
        T? Use<T>(Func<HsMemory, T> read);
    }

    /// <summary>
    /// Keeps one attachment to Hearthstone and transparently re-attaches if the game restarts. An attachment made
    /// while the game is still loading can miss classes the game hasn't loaded yet; its reads then quietly come back
    /// empty instead of failing. So a new attachment counts as unproven until a read returns something, and an
    /// unproven one is dropped (and made again on the next read) once it is <see cref="ProveWithin"/> old.
    /// </summary>
    public sealed class LiveMemory : ILiveMemory
    {
        private const int Attempts = 2;

        /// <summary>How long a new attachment may return only empty reads before it is made again.</summary>
        public static readonly TimeSpan ProveWithin = TimeSpan.FromSeconds(90);

        private readonly bool _verbose;
        private readonly Action<string>? _log;
        private readonly Func<DateTime> _now;
        private readonly object _lock = new object();
        private HsMemory? _mem;
        private DateTime _attachedAt;
        private bool _proven;

        /// <summary>Creates the holder; nothing is attached until the first <see cref="Use{T}"/>.</summary>
        /// <param name="verbose">Show UnitySpy's internal log and report failed reads.</param>
        /// <param name="log">Where failed reads are reported when <paramref name="verbose"/> is on, and stale attachments always; may be null.</param>
        /// <param name="now">The clock (UTC); null for the system clock.</param>
        public LiveMemory(bool verbose, Action<string>? log = null, Func<DateTime>? now = null)
        {
            _verbose = verbose;
            _log = log;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <inheritdoc />
        public T? Use<T>(Func<HsMemory, T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            lock (_lock)
            {
                for (var attempt = 0; attempt < Attempts; attempt++)
                {
                    try
                    {
                        if (!EnsureAttached()) return default;
                        var result = read(_mem);
                        Judge(!IsEmpty(result));
                        return result;
                    }
                    catch (Exception ex)
                    {
                        if (_verbose) _log?.Invoke("[memory] " + ex.Message);
                        Detach();
                    }
                }
                return default;
            }
        }

        /// <summary>
        /// True when an attachment that has returned only empty reads since <paramref name="attachedAt"/> should be
        /// made again: it never returned anything and is at least <see cref="ProveWithin"/> old.
        /// </summary>
        public static bool IsStale(bool proven, DateTime attachedAt, DateTime now) => !proven && now - attachedAt >= ProveWithin;

        /// <summary>A read "returned something": not null and not its type's default (0, an empty tuple...).</summary>
        private static bool IsEmpty<T>(T value) => value == null || EqualityComparer<T>.Default.Equals(value, default!);

        /// <summary>Marks the attachment proven after a useful read, or drops it when it has stayed empty too long.</summary>
        private void Judge(bool useful)
        {
            if (useful) { _proven = true; return; }
            if (!IsStale(_proven, _attachedAt, _now())) return;
            _log?.Invoke($"[memory] reads stayed empty for {ProveWithin.TotalSeconds:0} s after attaching (the game was probably still loading): attaching again");
            Detach();
        }

        /// <summary>Drops the attachment.</summary>
        public void Dispose()
        {
            lock (_lock) Detach();
        }

        /// <summary>Attaches when there's no live attachment. Returns false when Hearthstone isn't running.</summary>
        [MemberNotNullWhen(true, nameof(_mem))]
        private bool EnsureAttached()
        {
            if (_mem != null && IsAlive(_mem.ProcessId)) return true;
            Detach();
            if (!HsMemory.IsHearthstoneRunning) return false;
            _mem = HsMemory.Attach(_verbose);
            _attachedAt = _now();
            _proven = false;
            return true;
        }

        private void Detach()
        {
            _mem?.Dispose();
            _mem = null;
        }

        private static bool IsAlive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; }
            catch (ArgumentException) { return false; }          // no process with that id any more
            catch (InvalidOperationException) { return false; }  // the process went away while we looked
            catch (Win32Exception) { return false; }             // not allowed to look: treat as gone and re-attach
        }
    }
}
