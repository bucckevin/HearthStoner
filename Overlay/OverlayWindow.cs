using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HearthStoner
{
    /// <summary>
    /// A transparent, click-through, always-on-top window laid over the Hearthstone window. Opponent info sits on the
    /// left edge, your info on the right edge. It only reads the advice snapshot; it never sends input to the game.
    /// Note: Hearthstone must run in windowed or borderless-fullscreen mode for any overlay to be visible.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        private readonly Func<AdviceSnapshot> _source;
        // [0] left info, [1] left deck, [2] right info, [3] right deck.
        private readonly StackPanel[] _panels = { new StackPanel(), new StackPanel(), new StackPanel(), new StackPanel() };
        private readonly Border[] _boxes = new Border[4];
        private string _lastRendered = "";
        private readonly double _panelWidth;
        private readonly Advisor _advisor;
        private readonly Settings _settings;
        private readonly Action<string> _log;
        private bool _userHidden;

        /// <summary>Hidden by the user (hotkey or tray menu).</summary>
        public bool UserHidden { get => _userHidden; set { _userHidden = value; _lastRendered = ""; } }
        private bool _hoverMode;
        private readonly Canvas _handLayer = new Canvas { IsHitTestVisible = false };
        private string _lastHand = "";

        public OverlayWindow(Func<AdviceSnapshot> source, Advisor advisor, Settings settings, Action<string> log)
        {
            _source = source;
            _advisor = advisor;
            _settings = settings ?? new Settings();
            _log = log ?? (_ => { });
            _panelWidth = Math.Max(180, _settings.PanelWidth);
            var panelWidth = _panelWidth;
            var opacity = Math.Max(0.2, Math.Min(1.0, _settings.OverlayOpacity));

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "HearthStoner overlay";
            Left = 0; Top = 0; Width = 800; Height = 600;

            for (var i = 0; i < 4; i++) _boxes[i] = MakePanel(_panels[i], opacity);
            Content = Layout(SideColumn(_boxes[0], _boxes[1]), SideColumn(_boxes[2], _boxes[3]), _handLayer, _settings, panelWidth);

            SourceInitialized += (s, e) =>
            {
                MakeClickThrough(true);
                RegisterHotkeys();
            };
            Closed += (s, e) => UnregisterHotkeys();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (s, e) => Tick();
            timer.Start();
        }

        /// <summary>
        /// Arranges the two side columns over the game window. "right" = opponent and you side by side at the right
        /// edge (default; easiest on wide screens), "left" = both at the left edge, "split" = opponent on the left
        /// edge, you on the right edge. The columns move as one block, centred vertically, with their tops lined up.
        /// The hand marks layer covers the whole window. Shared with 'HearthStoner preview'.
        /// </summary>
        public static Grid Layout(FrameworkElement leftBox, FrameworkElement rightBox, Canvas handLayer, Settings settings, double panelWidth)
        {
            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var layout = (settings?.Layout ?? "right").Trim().ToLowerInvariant();
            ColumnDefinition Fixed() => new ColumnDefinition { Width = new GridLength(panelWidth) };
            ColumnDefinition Star() => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            if (layout == "split")
            {
                grid.ColumnDefinitions.Add(Fixed()); grid.ColumnDefinitions.Add(Star()); grid.ColumnDefinitions.Add(Fixed());
                Grid.SetColumn(leftBox, 0); Grid.SetColumn(rightBox, 2);
            }
            else if (layout == "left")
            {
                grid.ColumnDefinitions.Add(Fixed()); grid.ColumnDefinitions.Add(Fixed()); grid.ColumnDefinitions.Add(Star());
                Grid.SetColumn(leftBox, 0); Grid.SetColumn(rightBox, 1);
            }
            else
            {
                grid.ColumnDefinitions.Add(Star()); grid.ColumnDefinitions.Add(Fixed()); grid.ColumnDefinitions.Add(Fixed());
                Grid.SetColumn(leftBox, 1); Grid.SetColumn(rightBox, 2);
            }
            // Side by side, the two panels sit closer together.
            if (layout != "split")
            {
                leftBox.Margin = new Thickness(6, 20, 3, 20);
                rightBox.Margin = new Thickness(3, 20, 6, 20);
            }
            leftBox.VerticalAlignment = VerticalAlignment.Stretch;
            rightBox.VerticalAlignment = VerticalAlignment.Stretch;
            grid.Children.Add(leftBox);
            grid.Children.Add(rightBox);
            var root = new Grid();
            root.Children.Add(grid);
            if (handLayer != null) root.Children.Add(handLayer);
            return root;
        }

        private Border MakePanel(StackPanel content, double opacity) => PanelChrome(content, opacity, _panelWidth, _settings);

        /// <summary>The panel box around a column of sections (also used by 'HearthStoner preview').</summary>
        public static Border PanelChrome(StackPanel content, double opacity, double panelWidth, Settings settings)
        {
            var _panelWidth = panelWidth;
            var _settings = settings;
            // Fixed width so text still wraps inside the Viewbox (which measures its child with unlimited space).
            var scale = Math.Max(0.6, Math.Min(2.0, _settings?.FontScale ?? 1.0));
            content.Width = (_panelWidth - 2 * 6 - 2 * 8 - 2) / scale;
            if (Math.Abs(scale - 1.0) > 0.01) content.LayoutTransform = new ScaleTransform(scale, scale);
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 18, 20, 28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 90, 100, 130)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(6, 20, 6, 20),
                VerticalAlignment = VerticalAlignment.Center,
                // Content starts at the top when the panel is stretched to match a taller neighbour.
                Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = content, VerticalAlignment = VerticalAlignment.Top },
            };
        }

        /// <summary>
        /// One side of the overlay: the info panel, with the deck panel under it. Each is sized to its content and
        /// shrinks (never grows) if the window is too short.
        /// </summary>
        public static DockPanel SideColumn(Border info, Border deck)
        {
            info.Margin = new Thickness(0); info.VerticalAlignment = VerticalAlignment.Top;
            deck.Margin = new Thickness(0, 8, 0, 0); deck.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(info, Dock.Top);
            var col = new DockPanel { Margin = new Thickness(6, 20, 6, 20) };
            col.Children.Add(info);
            col.Children.Add(deck);
            return col;
        }

        /// <summary>Fills one side's info and deck panels (AdviceSection.Lower picks the panel) and hides empty ones.</summary>
        public static void FillSide(Border info, Border deck, IEnumerable<AdviceSection> sections, bool cardArt, bool hoverMode)
        {
            var list = sections.ToList();
            foreach (var (box, lower) in new[] { (info, false), (deck, true) })
            {
                var panel = (StackPanel)((Viewbox)box.Child).Child;
                panel.Children.Clear();
                SectionView.Fill(panel, list.Where(s => s.Lower == lower), cardArt, hoverMode);
                box.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Tick()
        {
            if (_userHidden || !PositionOverGame())
            {
                if (IsVisible) Hide();
                return;
            }
            if (!IsVisible) Show();

            AdviceSnapshot snap;
            try { snap = _source(); }
            catch (Exception ex) { snap = Error(ex); }
            Render(snap);
            RenderHand();
        }

        /// <summary>Re-places the marks over the opponent's hand when the hand or the window size changes.</summary>
        private void RenderHand()
        {
            List<HandMark> marks = null;
            if (_settings.ShowOpponentHandMarks && !_advisor.HideOpponent)
            {
                try { marks = _advisor.OpponentHandMarks(); }
                catch (Exception ex) { _log("[overlay] hand marks: " + ex.Message); }
            }
            var count = marks?.Count ?? 0;
            var key = $"{ActualWidth:F0}x{ActualHeight:F0}|" + (marks == null ? "" : string.Join(";", marks.Select(m => $"{m.Slot},{m.Turn},{m.Kind},{m.CardId},{m.Note}")));
            if (key == _lastHand) return;
            _lastHand = key;
            HandMarkView.Place(_handLayer, marks, count, ActualWidth, ActualHeight);
        }

        private static AdviceSnapshot Error(Exception ex)
        {
            var s = new AdviceSnapshot();
            var sec = new AdviceSection("HearthStoner error");
            sec.Add(ex.Message, Tone.Bad);
            s.Sections.Add(sec);
            return s;
        }

        private void Render(AdviceSnapshot snap)
        {
            // Skip rebuilding the visuals when nothing changed.
            var sb = new StringBuilder();
            sb.Append(CardTiles.Version).Append('\n');   // redraw when new card art has downloaded
            foreach (var sec in snap.Sections)
            {
                sb.Append(sec.Left ? "L|" : "R|").Append(sec.Lower ? "D|" : "").Append(sec.Title).Append('|').Append(sec.RightText).Append('\n');
                foreach (var l in sec.Lines) sb.Append((int)l.Tone).Append(l.Struck ? "~" : "").Append(l.Flash ? "!" : "").Append(l.Text).Append('\n');
            }
            sb.Append(_hoverMode ? "H" : "");
            var key = sb.ToString();
            if (key == _lastRendered) return;
            _lastRendered = key;

            FillSide(_boxes[0], _boxes[1], snap.Sections.Where(x => x.Left), _settings.CardArt, _hoverMode);
            FillSide(_boxes[2], _boxes[3], snap.Sections.Where(x => !x.Left), _settings.CardArt, _hoverMode);
            if (_hoverMode)
            {
                _boxes[2].Visibility = Visibility.Visible;
                _panels[2].Children.Insert(0, new TextBlock
                {
                    Text = $"Hover mode: point at a card for details. {_settings.HotkeyHoverMode} to give the mouse back to the game.",
                    Foreground = new SolidColorBrush(SectionView.ColorFor(Tone.Warn)), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                });
            }
        }

        // ------------------------------------------------------------------ window placement

        /// <summary>Covers the Hearthstone window's client area. False if the game isn't the active, visible window.</summary>
        private bool PositionOverGame()
        {
            var hwnd = FindHearthstoneWindow();
            if (hwnd == IntPtr.Zero || IsIconic(hwnd)) return false;
            var fg = GetForegroundWindow();
            var self = new WindowInteropHelper(this).Handle;
            if (fg != hwnd && fg != self) return false;

            if (!GetClientRect(hwnd, out var rc)) return false;
            var topLeft = new POINT { X = rc.Left, Y = rc.Top };
            if (!ClientToScreen(hwnd, ref topLeft)) return false;

            var dpi = VisualTreeHelper.GetDpi(this);
            var left = topLeft.X / dpi.DpiScaleX;
            var top = topLeft.Y / dpi.DpiScaleY;
            var width = (rc.Right - rc.Left) / dpi.DpiScaleX;
            var height = (rc.Bottom - rc.Top) / dpi.DpiScaleY;
            if (width < 2 * _panelWidth + 100 || height < 200) return false;

            if (Math.Abs(Left - left) > 0.5) Left = left;
            if (Math.Abs(Top - top) > 0.5) Top = top;
            if (Math.Abs(Width - width) > 0.5) Width = width;
            if (Math.Abs(Height - height) > 0.5) Height = height;
            return true;
        }

        private IntPtr _hsWindow;
        private DateTime _nextLookup;
        private IntPtr FindHearthstoneWindow()
        {
            if (_hsWindow != IntPtr.Zero && IsWindow(_hsWindow)) return _hsWindow;
            if (DateTime.UtcNow < _nextLookup) return IntPtr.Zero;
            _nextLookup = DateTime.UtcNow.AddSeconds(2);
            var p = Process.GetProcessesByName("Hearthstone").FirstOrDefault();
            _hsWindow = p?.MainWindowHandle ?? IntPtr.Zero;
            return _hsWindow;
        }

        /// <summary>Click-through on: mouse goes to the game. Off (hover mode): the overlay gets the mouse.</summary>
        private void MakeClickThrough(bool on)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            ex = on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        // ------------------------------------------------------------------ hotkeys

        private const int WM_HOTKEY = 0x0312;
        private readonly System.Collections.Generic.Dictionary<int, Action> _hotkeys = new System.Collections.Generic.Dictionary<int, Action>();

        private void RegisterHotkeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            var id = 0x4853;   // "HS"
            void Reg(string combo, string what, Action action)
            {
                if (string.IsNullOrWhiteSpace(combo)) return;
                if (!ParseHotkey(combo, out var mods, out var vk)) { _log($"[overlay] Can't read hotkey '{combo}' for {what}."); return; }
                id++;
                if (RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk)) _hotkeys[id] = action;
                else _log($"[overlay] Hotkey {combo} ({what}) is already used by another program; change it in {Settings.Path}.");
            }
            Reg(_settings.HotkeyToggleOverlay, "show/hide overlay", () => _userHidden = !_userHidden);
            Reg(_settings.HotkeyToggleDeck, "deck list", () => { if (_advisor != null) _advisor.HideDeck = !_advisor.HideDeck; });
            Reg(_settings.HotkeyToggleOpponent, "opponent panel", () => { if (_advisor != null) _advisor.HideOpponent = !_advisor.HideOpponent; });
            Reg(_settings.HotkeyToggleAdvice, "advice", () => { if (_advisor != null) _advisor.HideAdvice = !_advisor.HideAdvice; });
            Reg(_settings.HotkeyHoverMode, "hover mode", () => { _hoverMode = !_hoverMode; MakeClickThrough(!_hoverMode); });
        }

        private void UnregisterHotkeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            foreach (var id in _hotkeys.Keys) UnregisterHotKey(hwnd, id);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var action))
            {
                action();
                _lastRendered = "";   // force a redraw
                Tick();
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>"Ctrl+Shift+H", "Alt+F2", "Win+D" -> modifier flags and virtual-key code.</summary>
        public static bool ParseHotkey(string combo, out uint mods, out uint vk) => Hotkeys.Parse(combo, out mods, out vk);

        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        /// <summary>Runs the overlay on its own UI thread; returns immediately.</summary>
        public static Thread Start(Func<AdviceSnapshot> source, Advisor advisor, Settings settings, Action<string> log)
        {
            var t = new Thread(() =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var w = new OverlayWindow(source, advisor, settings, log);
                w.Show();
                app.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            return t;
        }

        // ------------------------------------------------------------------ Win32

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    }
}
