using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HearthStoner.Pages
{
    /// <summary>
    /// The Play page's game button. Hearthstone closed: "Launch Hearthstone" (starts it through Battle.net).
    /// Launching: "Starting Hearthstone..." until the game appears. Running: a green dot and "Switch to Hearthstone",
    /// which brings the game window to the front. Checks every 1.5 seconds while it's on screen.
    /// </summary>
    public sealed class GameButton : Button
    {
        public enum State { Closed, Starting, Running }

        /// <summary>For screenshots: show this state instead of the real one.</summary>
        public static State? Override;

        private readonly AppShell _shell;
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        private State _state = (State)(-1);
        private DateTime _startedAt;

        public GameButton(AppShell shell)
        {
            _shell = shell;
            HorizontalAlignment = HorizontalAlignment.Left;
            Click += (s, e) => HandleClick();
            _timer.Tick += (s, e) => Refresh();
            Loaded += (s, e) => { Refresh(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
            Refresh();
        }

        private void Refresh()
        {
            var running = Override.HasValue ? Override == State.Running : GameLauncher.IsRunning;
            var next = Override ?? (running ? State.Running : _state == State.Starting ? State.Starting : State.Closed);
            if (next == State.Starting && !Override.HasValue && DateTime.UtcNow - _startedAt > TimeSpan.FromMinutes(2))
            {
                next = State.Closed;
                _shell.Status?.Invoke("Hearthstone didn't start. Check the Battle.net app, or choose how to start it in Settings.");
            }
            if (next != _state) Show(next);
        }

        private void Show(State s)
        {
            _state = s;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            switch (s)
            {
                case State.Closed:
                    Style = (Style)Theme.Styles["Primary"];
                    row.Children.Add(new Path { Data = Geometry.Parse("M 0,0 L 8,5 L 0,10 Z"), Fill = Theme.B(Theme.Rgb(0x0B, 0x14, 0x30)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                    row.Children.Add(new TextBlock { Text = "Launch Hearthstone", VerticalAlignment = VerticalAlignment.Center });
                    ToolTip = "Starts Hearthstone through the Battle.net app";
                    IsEnabled = true;
                    break;
                case State.Starting:
                    Style = (Style)Theme.Styles["Ghost"];
                    row.Children.Add(new TextBlock { Text = "Starting Hearthstone...", VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Dusk });
                    ToolTip = null;
                    IsEnabled = false;
                    break;
                default:
                    Style = (Style)Theme.Styles["Ghost"];
                    row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.Win, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
                        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Theme.WinC, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 } });
                    row.Children.Add(new TextBlock { Text = "Switch to Hearthstone", VerticalAlignment = VerticalAlignment.Center });
                    ToolTip = "Hearthstone is running. Click to bring it to the front.";
                    IsEnabled = true;
                    break;
            }
            Content = row;
        }

        private void HandleClick()
        {
            if (_state == State.Running)
            {
                if (!GameLauncher.BringToFront()) Refresh();
                return;
            }
            if (_state != State.Closed) return;
            var error = GameLauncher.Launch(_shell.Data?.Services?.Settings?.LaunchPath);
            if (error != null) { _shell.Status?.Invoke(error); return; }
            _startedAt = DateTime.UtcNow;
            Show(State.Starting);
        }
    }
}
