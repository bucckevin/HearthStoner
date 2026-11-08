using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HearthStoner.Overlay
{
    /// <summary>
    /// 'HearthStoner preview [out.png] [--log Power.log] [--turn N]': replays a saved game up to turn N and renders the
    /// overlay's two panels to a PNG, exactly as the overlay draws them. For checking the look without playing.
    /// </summary>
    public static class Preview
    {
        /// <summary>A saved game replayed up to a turn, ready to draw the overlay as it was then with any settings.</summary>
        public sealed class Game
        {
            /// <summary>Works out the overlay's sections from the game state; it reads the settings it was given each time.</summary>
            public Advisor Advisor;
            /// <summary>The log folder the game came from ("Hearthstone_2026_11_08_00_12_03").</summary>
            public string Source;
            public int Turn;
        }

        /// <summary>The newest Power.log with a game in it in Hearthstone's log folders, or null.</summary>
        public static string LatestPowerLog(string install)
        {
            install = install ?? HearthstoneLocator.FindInstallDir(null);
            if (install == null || !Directory.Exists(Path.Combine(install, "Logs"))) return null;
            return Directory.GetDirectories(Path.Combine(install, "Logs"), "Hearthstone_*")
                .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        /// <summary>
        /// Replays <paramref name="powerLog"/> up to <paramref name="turn"/> (or its end) with the deck you queued for it.
        /// Slow (it reads the whole log): run it off the UI thread.
        /// </summary>
        public static Game Prepare(string powerLog, int turn, CardDb cards, MetaStats meta, Settings settings, Action<string> log)
        {
            if (string.IsNullOrEmpty(powerLog)) throw new ArgumentException("A Power.log is needed.", nameof(powerLog));
            var queued = DecksLog.LastQueuedDeck(powerLog);
            var deck = queued == null ? null : DeckCode.Decode(queued.DeckCode, cards);
            return Prepare(GameLog.ReadShared(powerLog).ToList(), deck, queued?.Name, Path.GetFileName(Path.GetDirectoryName(powerLog)), turn, cards, meta, settings, log);
        }

        /// <summary>
        /// Replays one of your saved games (its own log and deck) up to <paramref name="turn"/>. Null when its log is
        /// gone. Slow: run it off the UI thread.
        /// </summary>
        public static Game Prepare(GameRecord game, int turn, CardDb cards, MetaStats meta, Settings settings, Action<string> log)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            var lines = GameLog.Load(game);
            if (lines == null || lines.Count == 0) return null;
            var deck = game.DeckCards != null && game.DeckCards.Count > 0 ? game.DeckCards : null;
            return Prepare(lines, deck, game.DeckName, $"{game.Start:MMM d} game vs {game.OpponentArchetype ?? MetaStats.ClassName(game.OpponentClass)}", turn, cards, meta, settings, log);
        }

        /// <summary>Feeds the game's log lines to a tracker with your deck, stopping a turn before the end (past it the overlay shows its between-games panel).</summary>
        private static Game Prepare(List<string> lines, IReadOnlyCollection<string> deck, string deckName, string source, int turn, CardDb cards, MetaStats meta, Settings settings, Action<string> log)
        {
            log = log ?? (_ => { });
            GameTracker tracker = null;
            tracker = new GameTracker(cards, ev =>
            {
                if (ev.Kind == GameEventKind.GameStart && deck != null) { tracker.FriendlyDeckList = deck.ToList(); tracker.FriendlyDeckName = deckName; }
            });
            var advisor = new Advisor(tracker, cards, meta, settings);
            var prepared = false;
            var target = Math.Max(1, Math.Min(turn, LastTurn(lines) - 1));
            foreach (var line in lines)
            {
                tracker.Feed(line);
                if (!prepared && deck != null && tracker.FriendlyPlayerId != 0) { advisor.PrepareFriendlyDeck(log); prepared = true; }
                if (tracker.InGame && tracker.Turn >= target) break;
            }
            return new Game { Advisor = advisor, Source = source, Turn = tracker.Turn };
        }

        /// <summary>The last turn of the first game in these log lines (its GameEntity TURN before the game completes); int.MaxValue if they don't say.</summary>
        public static int LastTurn(IEnumerable<string> lines)
        {
            var last = 0;
            foreach (var line in lines)
            {
                if (!line.Contains("Entity=GameEntity tag=")) continue;
                if (line.Contains("tag=STATE value=COMPLETE") && last > 0) break;
                var i = line.IndexOf("tag=TURN value=", StringComparison.Ordinal);
                if (i >= 0 && int.TryParse(line.Substring(i + 15).Trim(), out var t)) last = Math.Max(last, t);
            }
            return last > 0 ? last : int.MaxValue;
        }

        public static int Run(string outPng, string powerLog, int turn, string rank, string period, Action<string> log)
        {
            var settings = Settings.Load();
            var cards = CardDb.Load();
            powerLog = powerLog ?? LatestPowerLog(null);
            if (powerLog == null) { log("No Power.log with a game found."); return 1; }
            var meta = MetaStats.Load("standard", rank ?? "legend", period ?? "past-7", log);
            var game = Prepare(powerLog, turn, cards, meta, settings, log);
            var snap = game.Advisor.Compute();
            var hand = game.Advisor.OpponentHandMarks();
            log($"{game.Source}, turn {game.Turn}: {snap.Sections.Count} sections");

            Exception error = null;
            var t = new Thread(() =>
            {
                try
                {
                    Render(snap, settings, outPng);
                    var dir = Path.GetDirectoryName(Path.GetFullPath(outPng)) ?? ".";
                    var stem = Path.GetFileNameWithoutExtension(outPng);
                    RenderHand(hand, Path.Combine(dir, stem + "-hand.png"));
                    RenderOverlay(snap, hand, settings, Path.Combine(dir, stem + "-overlay.png"));
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw error;
            log("Wrote " + Path.GetFullPath(outPng));
            return 0;
        }

        /// <summary>The marks over the opponent's hand on a 1920x1080 frame, with a stand-in card back under each.</summary>
        private static void RenderHand(List<HandMark> marks, string outPng)
        {
            const double w = 1920, h = 1080;
            var canvas = new Canvas { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(52, 44, 36)) };
            // The 4:3 board area, for reference.
            var board = (4.0 / 3.0) / (w / h);
            var area = new System.Windows.Shapes.Rectangle { Width = w * board, Height = h, Stroke = new SolidColorBrush(Color.FromRgb(90, 80, 70)), StrokeThickness = 2 };
            Canvas.SetLeft(area, w * (1 - board) / 2);
            canvas.Children.Add(area);
            for (var i = 0; i < marks.Count; i++)
            {
                var (x, y) = HandGeometry.Position(marks.Count, i, w, h);
                var back = new System.Windows.Shapes.Rectangle { Width = 70, Height = 100, RadiusX = 6, RadiusY = 6, Fill = new SolidColorBrush(Color.FromRgb(110, 70, 40)), Stroke = Brushes.Black };
                Canvas.SetLeft(back, x - 35); Canvas.SetTop(back, y - 70);
                canvas.Children.Add(back);
            }
            var marksLayer = new Canvas { Width = w, Height = h };
            HandMarkView.Place(marksLayer, marks, marks.Count, w, h);
            canvas.Children.Add(marksLayer);
            canvas.Measure(new Size(w, h));
            canvas.Arrange(new Rect(0, 0, w, h));
            canvas.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(canvas);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(outPng)) enc.Save(f);
        }

        /// <summary>The whole overlay as it sits over a 1920x1080 game window: both panels in their layout, and the hand marks.</summary>
        private static void RenderOverlay(AdviceSnapshot snap, List<HandMark> marks, Settings settings, string outPng)
        {
            const double w = 1920, h = 1080;
            var frame = OverlayFrame(snap, marks, settings);
            frame.Measure(new Size(w, h));
            frame.Arrange(new Rect(0, 0, w, h));
            frame.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(frame);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(outPng)) enc.Save(f);
        }

        /// <summary>
        /// The overlay over a 1920x1080 stand-in for the game (a dark board with the 4:3 play area marked): both panels
        /// in the chosen layout, and the hand marks when they're on. Drawn with <paramref name="settings"/> as they are now.
        /// </summary>
        public static Grid OverlayFrame(AdviceSnapshot snap, List<HandMark> marks, Settings settings)
        {
            const double w = 1920, h = 1080;
            var width = Math.Max(180, settings.PanelWidth);
            var opacity = Math.Max(0.2, Math.Min(1.0, settings.OverlayOpacity));
            var hand = new Canvas { Width = w, Height = h };
            if (settings.ShowOpponentHandMarks) HandMarkView.Place(hand, marks, marks.Count, w, h);
            var root = OverlayWindow.Layout(Side(snap, true, opacity, width, settings), Side(snap, false, opacity, width, settings), hand, settings, width);
            var frame = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(46, 40, 34)) };
            var board = (4.0 / 3.0) / (w / h);
            frame.Children.Add(new System.Windows.Shapes.Rectangle { Width = w * board, Fill = new SolidColorBrush(Color.FromRgb(70, 58, 44)), HorizontalAlignment = HorizontalAlignment.Center });
            frame.Children.Add(root);
            return frame;
        }

        /// <summary>The overlay's two sides next to each other at their real size (opponent's left, yours right), drawn with <paramref name="settings"/>.</summary>
        public static StackPanel Panels(AdviceSnapshot snap, Settings settings)
        {
            var width = Math.Max(180, settings.PanelWidth);
            var opacity = Math.Max(0.2, Math.Min(1.0, settings.OverlayOpacity));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var left in new[] { true, false })
            {
                var col = Side(snap, left, opacity, width, settings);
                col.Width = width;
                col.Margin = new Thickness(0, 0, 12, 0);
                col.VerticalAlignment = VerticalAlignment.Top;
                row.Children.Add(col);
            }
            return row;
        }

        /// <summary>One side of the overlay as it draws in the game: info panel over deck panel.</summary>
        private static DockPanel Side(AdviceSnapshot snap, bool left, double opacity, double width, Settings settings)
        {
            var info = OverlayWindow.PanelChrome(new StackPanel(), opacity, width, settings);
            var deck = OverlayWindow.PanelChrome(new StackPanel(), opacity, width, settings);
            OverlayWindow.FillSide(info, deck, snap.Sections.Where(s => s.Left == left), settings.CardArt, false);
            return OverlayWindow.SideColumn(info, deck);
        }

        private static void Render(AdviceSnapshot snap, Settings settings, string outPng)
        {
            // Card art downloads in the background the first time: ask for every tile, wait until they're in.
            foreach (var l in snap.Sections.SelectMany(s => s.Lines).Where(l => l.CardId != null)) CardTiles.Get(l.CardId);
            var v = -1;
            for (var i = 0; i < 40 && v != CardTiles.Version; i++) { v = CardTiles.Version; Thread.Sleep(500); }

            var row = Panels(snap, settings);
            row.Background = new SolidColorBrush(Color.FromRgb(70, 60, 50));
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            row.Arrange(new Rect(row.DesiredSize));
            row.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(row.ActualWidth), (int)Math.Ceiling(row.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(row);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(outPng)) enc.Save(f);
        }
    }
}
