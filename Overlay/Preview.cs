using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HearthStoner
{
    /// <summary>
    /// 'HearthStoner preview [out.png] [--log Power.log] [--turn N]': replays a saved game up to turn N and renders the
    /// overlay's two panels to a PNG, exactly as the overlay draws them. For checking the look without playing.
    /// </summary>
    public static class Preview
    {
        public static int Run(string outPng, string powerLog, int turn, string rank, string period, Action<string> log)
        {
            var settings = Settings.Load();
            var cards = CardDb.Load();
            if (powerLog == null)
            {
                var install = HearthstoneLocator.FindInstallDir(null);
                var logs = install == null ? new List<string>() : Directory.GetDirectories(Path.Combine(install, "Logs"), "Hearthstone_*")
                    .Select(d => Path.Combine(d, "Power.log")).Where(File.Exists).Where(f => new FileInfo(f).Length > 20000)
                    .OrderByDescending(File.GetLastWriteTimeUtc).ToList();
                powerLog = logs.FirstOrDefault();
            }
            if (powerLog == null) { log("No Power.log with a game found."); return 1; }
            var meta = MetaStats.Load("standard", rank ?? "legend", period ?? "past-7", log);

            var queued = DecksLog.LastQueuedDeck(powerLog);
            var deck = queued == null ? null : DeckCode.Decode(queued.DeckCode, cards);
            GameTracker tracker = null;
            tracker = new GameTracker(cards, ev =>
            {
                if (ev.Kind == GameEventKind.GameStart && deck != null) { tracker.FriendlyDeckList = deck.ToList(); tracker.FriendlyDeckName = queued.Name; }
            });
            var advisor = new Advisor(tracker, cards, meta, settings);
            var prepared = false;
            foreach (var line in GameLog.ReadShared(powerLog))
            {
                tracker.Feed(line);
                if (!prepared && deck != null && tracker.FriendlyPlayerId != 0) { advisor.PrepareFriendlyDeck(log); prepared = true; }
                if (tracker.InGame && tracker.Turn >= turn) break;
            }
            var snap = advisor.Compute();
            var hand = advisor.OpponentHandMarks();
            log($"{Path.GetFileName(Path.GetDirectoryName(powerLog))}, turn {tracker.Turn}: {snap.Sections.Count} sections");

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
            var width = Math.Max(180, settings.PanelWidth);
            var opacity = Math.Max(0.2, Math.Min(1.0, settings.OverlayOpacity));
            var hand = new Canvas { Width = w, Height = h };
            HandMarkView.Place(hand, marks, marks.Count, w, h);
            var root = OverlayWindow.Layout(Side(snap, true, opacity, width, settings), Side(snap, false, opacity, width, settings), hand, settings, width);
            // A stand-in for the game: dark board with the 4:3 play area marked.
            var frame = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(46, 40, 34)) };
            var board = (4.0 / 3.0) / (w / h);
            frame.Children.Add(new System.Windows.Shapes.Rectangle { Width = w * board, Fill = new SolidColorBrush(Color.FromRgb(70, 58, 44)), HorizontalAlignment = HorizontalAlignment.Center });
            frame.Children.Add(root);
            frame.Measure(new Size(w, h));
            frame.Arrange(new Rect(0, 0, w, h));
            frame.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(frame);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(outPng)) enc.Save(f);
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

            var width = Math.Max(180, settings.PanelWidth);
            var opacity = Math.Max(0.2, Math.Min(1.0, settings.OverlayOpacity));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(70, 60, 50)) };
            foreach (var left in new[] { true, false })
            {
                var col = Side(snap, left, opacity, width, settings);
                col.Width = width;
                row.Children.Add(col);
            }
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
