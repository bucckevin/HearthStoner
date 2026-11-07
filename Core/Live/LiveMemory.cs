using System;
using System.ComponentModel;
using System.Diagnostics;

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
        T Use<T>(Func<HsMemory, T> read);
    }

    /// <summary>Keeps one attachment to Hearthstone and transparently re-attaches if the game restarts.</summary>
    public sealed class LiveMemory : ILiveMemory
    {
        private const int Attempts = 2;
        private readonly bool _verbose;
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private HsMemory _mem;

        /// <summary>Creates the holder; nothing is attached until the first <see cref="Use{T}"/>.</summary>
        /// <param name="verbose">Show UnitySpy's internal log and report failed reads.</param>
        /// <param name="log">Where failed reads are reported when <paramref name="verbose"/> is on; may be null.</param>
        public LiveMemory(bool verbose, Action<string> log = null)
        {
            _verbose = verbose;
            _log = log;
        }

        /// <inheritdoc />
        public T Use<T>(Func<HsMemory, T> read)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            lock (_lock)
            {
                for (var attempt = 0; attempt < Attempts; attempt++)
                {
                    try
                    {
                        if (!EnsureAttached()) return default;
                        return read(_mem);
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

        /// <summary>Drops the attachment.</summary>
        public void Dispose()
        {
            lock (_lock) Detach();
        }

        /// <summary>Attaches when there's no live attachment. Returns false when Hearthstone isn't running.</summary>
        private bool EnsureAttached()
        {
            if (_mem != null && IsAlive(_mem.ProcessId)) return true;
            Detach();
            if (!HsMemory.IsHearthstoneRunning) return false;
            _mem = HsMemory.Attach(_verbose);
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
