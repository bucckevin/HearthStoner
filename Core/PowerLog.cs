using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HearthStoner
{
    /// <summary>Follows the newest Power.log (switching when Hearthstone starts a new session folder) and feeds lines to a tracker.</summary>
    public sealed class PowerLogTailer
    {
        private readonly string _installDir;
        private readonly GameTracker _tracker;
        private volatile string _path;

        /// <summary>The Power.log being followed right now (null until one is found).</summary>
        public string CurrentPath => _path;
        private long _pos;
        private readonly StringBuilder _partial = new StringBuilder();

        public PowerLogTailer(string installDir, GameTracker tracker)
        {
            _installDir = installDir;
            _tracker = tracker;
        }

        public void Run(CancellationToken ct, Action<string> status)
        {
            var lastCheck = DateTime.MinValue;
            var firstFile = true;

            while (!ct.IsCancellationRequested)
            {
                if (DateTime.UtcNow - lastCheck > TimeSpan.FromSeconds(2))
                {
                    lastCheck = DateTime.UtcNow;
                    var latest = HearthstoneLocator.FindLatestPowerLog(_installDir);
                    if (latest != null && !string.Equals(latest, _path, StringComparison.OrdinalIgnoreCase))
                    {
                        _path = latest;
                        _pos = 0;
                        _partial.Clear();
                        status($"Watching {_path}");
                        // Replay the existing file silently so we know the state of a game already in progress.
                        _tracker.Quiet = firstFile;
                        ReadNew();
                        _tracker.Quiet = false;
                        firstFile = false;
                        if (_tracker.InGame) status($"Joined a game in progress (turn {(_tracker.Turn + 1) / 2}).");
                    }
                }

                if (_path != null) ReadNew();
                Thread.Sleep(200);
            }
        }

        private void ReadNew()
        {
            if (!File.Exists(_path)) return;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length < _pos) { _pos = 0; _partial.Clear(); }   // file was truncated / recreated
                if (fs.Length == _pos) return;
                fs.Seek(_pos, SeekOrigin.Begin);
                var buf = new byte[fs.Length - _pos];
                var read = 0;
                while (read < buf.Length)
                {
                    var r = fs.Read(buf, read, buf.Length - read);
                    if (r <= 0) break;
                    read += r;
                }
                _pos += read;
                _partial.Append(Encoding.UTF8.GetString(buf, 0, read));
            }

            var text = _partial.ToString();
            var lastNl = text.LastIndexOf('\n');
            if (lastNl < 0) return;
            _partial.Clear();
            _partial.Append(text.Substring(lastNl + 1));   // keep incomplete trailing line for next time
            // The overlay reads tracker state from the UI thread; hold the lock while a batch is applied.
            lock (_tracker.Sync)
            {
                foreach (var line in text.Substring(0, lastNl).Split('\n'))
                    _tracker.Feed(line.TrimEnd('\r'));
            }
        }
    }
}
