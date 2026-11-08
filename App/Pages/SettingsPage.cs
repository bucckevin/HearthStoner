using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner.Pages
{
    /// <summary>Settings: the overlay, meta data, startup and your collection, saved as you change them.</summary>
    public static class SettingsPage
    {
        public static UIElement Build(AppShell shell)
        {
            var st = shell.Data.Services.Settings ?? new Settings();
            void Save() { try { st.Save(); } catch (Exception swallowed) { Diag.Ignored(swallowed, "SettingsPage"); } }
            var sections = new List<StackPanel>();
            StackPanel p;

            p = Section(sections, "Display", "Changes apply right away.");
            p.Children.Add(Choice("Text size", "Everything in the window grows or shrinks. Bigger suits a large screen.",
                Settings.AppScales.Select(v => (v.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{v * 100:0}%")).ToArray(),
                Closest(st.AppScale, Settings.AppScales),
                v => { st.AppScale = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); Save(); shell.SetScale(st.AppScale); }));
            p.Children.Add(Toggle("Remember the window's size and place", "Opens where you left it, maximized if it was. Back to the middle if that screen is gone.", st.RememberWindow, v => { st.RememberWindow = v; Save(); }));
            p.Children.Add(KeyRow("Go to a page (in the order of the menu)", "Ctrl+1 … Ctrl+9"));

            p = Section(sections, "Overlay", "Panel and layout changes apply the next time the overlay starts; the rest apply right away.", left: true);
            p.Children.Add(Choice("Where the panels go", "Both at the right edge suits wide screens.",
                new[] { ("left", "Left"), ("right", "Right"), ("split", "Opponent left, you right") }, st.Layout ?? "right", v => { st.Layout = v; Save(); }));
            p.Children.Add(Choice("Panel background", null,
                new[] { ("0.6", "Light"), ("0.82", "Medium"), ("0.95", "Solid") }, Closest(st.OverlayOpacity, 0.6, 0.82, 0.95), v => { st.OverlayOpacity = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); Save(); }));
            p.Children.Add(Toggle("Card art in lists", null, st.CardArt, v => { st.CardArt = v; Save(); }));
            p.Children.Add(Toggle("Draw odds in your deck list", "Each card's chance to be your next draw.", st.DeckShowOdds, v => { st.DeckShowOdds = v; Save(); }));
            p.Children.Add(Toggle("Marks over the opponent's hand", "The turn each card arrived, mulligan keeps, created and returned cards.", st.ShowOpponentHandMarks, v => { st.ShowOpponentHandMarks = v; Save(); }));

            p = Section(sections, "Panels", "What the overlay shows. Hotkeys hide whole sides while you play.", left: true);
            p.Children.Add(Toggle("Health and damage", null, st.ShowHealth, v => { st.ShowHealth = v; Save(); }));
            p.Children.Add(Toggle("Your deck", null, st.ShowDeck, v => { st.ShowDeck = v; Save(); }));
            p.Children.Add(Toggle("Opponent: likely deck and hand", null, st.ShowOpponent, v => { st.ShowOpponent = v; Save(); }));
            p.Children.Add(Toggle("Opponent: cards played", null, st.ShowOpponentPlayed, v => { st.ShowOpponentPlayed = v; Save(); }));
            p.Children.Add(Toggle("Secrets", null, st.ShowSecrets, v => { st.ShowSecrets = v; Save(); }));
            p.Children.Add(Toggle("Mulligan and matchup tips", null, st.ShowMulligan, v => { st.ShowMulligan = v; Save(); }));
            p.Children.Add(Toggle("This turn: ways to spend your mana", null, st.ShowThisTurn, v => { st.ShowThisTurn = v; Save(); }));
            p.Children.Add(Toggle("Board: threats, answers and lethal", null, st.ShowBoard, v => { st.ShowBoard = v; Save(); }));
            p.Children.Add(Toggle("Discover picks", null, st.ShowDiscover, v => { st.ShowDiscover = v; Save(); }));
            p.Children.Add(Toggle("Last game's review between games", null, st.ShowLastGameReview, v => { st.ShowLastGameReview = v; Save(); }));

            // The boards rendered from your Hearthstone install (GameArtBuilder): click one to use it in replays.
            var boards = GameArt.Boards();
            p = Section(sections, "Replay board", "The board drawn behind replays, rendered from your Hearthstone install.", left: true);
            if (boards.Count > 0)
            {
                // The board in use and a button; the grid of every board only opens when you want to change it.
                var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 4), Visibility = Visibility.Collapsed };
                var current = boards.FirstOrDefault(b => string.Equals(b, st.ReplayBoard, StringComparison.OrdinalIgnoreCase)) ?? boards[0];
                var currentPic = new Image { Source = GameArt.Board(current, 320), Width = 120, Height = 90, Stretch = Stretch.UniformToFill };
                var currentName = Theme.Text(GameArt.BoardName(current), 14, Theme.Vellum, FontWeights.SemiBold);
                Button change = null;
                change = Theme.Button("Change board", () =>
                {
                    var open = wrap.Visibility != Visibility.Visible;
                    wrap.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    change.Content = open ? "Close" : "Change board";
                });
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
                info.Children.Add(currentName);
                change.Margin = new Thickness(0, 10, 0, 0); change.HorizontalAlignment = HorizontalAlignment.Left;
                info.Children.Add(change);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new RoundedBorder { CornerRadius = new CornerRadius(6), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = currentPic });
                row.Children.Add(info);
                p.Children.Add(row);
                foreach (var root in boards)
                {
                    var stack = new StackPanel();
                    stack.Children.Add(new Image { Source = GameArt.Board(root, 320), Width = 156, Height = 117, Stretch = Stretch.UniformToFill });
                    var name = Theme.Text(GameArt.BoardName(root), 12.5, Theme.Vellum);
                    name.Margin = new Thickness(2, 4, 0, 0);
                    stack.Children.Add(name);
                    var tile = new Border
                    {
                        Width = 168, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                        BorderBrush = root == current ? Theme.Gold : Theme.Line, Background = Theme.Slate, Cursor = System.Windows.Input.Cursors.Hand, Child = stack,
                    };
                    tile.MouseLeftButtonUp += (s, e) =>
                    {
                        st.ReplayBoard = root; Save();
                        foreach (Border t in wrap.Children) t.BorderBrush = Theme.Line;
                        tile.BorderBrush = Theme.Gold;
                        currentPic.Source = GameArt.Board(root, 320);
                        currentName.Text = GameArt.BoardName(root);
                        wrap.Visibility = Visibility.Collapsed;
                        change.Content = "Change board";
                    };
                    wrap.Children.Add(tile);
                }
                p.Children.Add(wrap);
            }
            p.Children.Add(ArtStatus(shell));

            p = Section(sections, "Meta data", "Which games the win rates and archetypes come from (Firestone's public stats).");
            p.Children.Add(Choice("Rank bracket", "Automatic follows your rank.",
                new[] { ("auto", "Automatic"), ("all", "All"), ("diamond", "Diamond"), ("legend", "Legend"), ("top-2000-legend", "Top 2000") }, st.MetaRank ?? "auto",
                v => { st.MetaRank = v; Save(); shell.Data.Services.ReloadMeta?.Invoke(); shell.ReloadRequested?.Invoke(); }));
            p.Children.Add(Choice("Period", null,
                new[] { ("past-3", "3 days"), ("past-7", "7 days"), ("past-20", "20 days"), ("last-patch", "Since the patch") }, st.MetaPeriod ?? "past-7",
                v => { st.MetaPeriod = v; Save(); shell.Data.Services.ReloadMeta?.Invoke(); shell.ReloadRequested?.Invoke(); }));

            p = Section(sections, "Starting Hearthstone", "The Launch button on the Play page starts the game through the Battle.net app.");
            var target = GameLauncher.Resolve(st.LaunchPath);
            var found = Theme.Text(target.Path == null
                ? "Battle.net wasn't found. Choose Battle.net.exe (or Hearthstone.exe) below."
                : $"{(target.IsBattleNet ? "Battle.net" : GameLauncher.FileName(target.Path))}, found in {target.Source}:", 13.5, target.Path == null ? Theme.Gold : Theme.Dusk, wrap: true);
            p.Children.Add(found);
            if (target.Path != null)
            {
                var path = Theme.Text(target.Path, 13.5, Theme.Vellum, wrap: true);
                path.Margin = new Thickness(0, 2, 0, 0);
                p.Children.Add(path);
            }
            var launchButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            launchButtons.Children.Add(Theme.Button("Choose program...", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose Battle.net.exe or Hearthstone.exe",
                    Filter = "Battle.net or Hearthstone|Battle.net.exe;Hearthstone.exe|Programs (*.exe)|*.exe",
                    InitialDirectory = target.Path != null ? Path.GetDirectoryName(target.Path) : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                };
                if (dlg.ShowDialog() == true) { st.LaunchPath = dlg.FileName; Save(); shell.Redraw(); }
            }, primary: false));
            if (!string.IsNullOrEmpty(st.LaunchPath))
            {
                var auto = Theme.Button("Find it automatically", () => { st.LaunchPath = null; Save(); shell.Redraw(); }, primary: false);
                auto.Margin = new Thickness(8, 0, 0, 0);
                launchButtons.Children.Add(auto);
            }
            p.Children.Add(launchButtons);

            p = Section(sections, "App", null);
            p.Children.Add(Toggle("Start with Windows", "Sits in the tray and shows the overlay when Hearthstone is open.", Autostart.IsEnabled, v => { Autostart.Set(v); st.StartWithWindows = v; Save(); }));
            p.Children.Add(Toggle("Check for updates", "Once a day: new HearthStoner versions, the memory reader and Hearthstone's version.", st.CheckForUpdates, v => { st.CheckForUpdates = v; Save(); }));
            p.Children.Add(UpdateRow());

            p = Section(sections, "Your collection", null);
            var col = shell.Data.Collection;
            var colText = Theme.Text(col != null ? $"{col.Owned.Count:N0} different cards, read {col.Updated:MMM d, h:mm tt}. Kept up to date by itself while Hearthstone is open (after packs, crafting and games)." : "Not read yet. It's read by itself once Hearthstone is open.", 13.5, Theme.Dusk, wrap: true);
            colText.Margin = new Thickness(0, 0, 0, 10);
            p.Children.Add(colText);

            p = Section(sections, "Hotkeys", "While Hearthstone is in front. Change them in the settings file.");
            foreach (var (label, key) in new[] { ("Show or hide the overlay", st.HotkeyToggleOverlay), ("Your deck list", st.HotkeyToggleDeck), ("Opponent panels and hand marks", st.HotkeyToggleOpponent), ("Advice panels", st.HotkeyToggleAdvice), ("Hover mode: point at cards for details", st.HotkeyHoverMode) })
                p.Children.Add(KeyRow(label, key));

            p = Section(sections, "Files", null);
            var files = new StackPanel { Orientation = Orientation.Horizontal };
            var openSettings = Theme.Button("Open settings file", () => { try { Process.Start(new ProcessStartInfo(Settings.Path) { UseShellExecute = true }); } catch (Exception swallowed) { Diag.Ignored(swallowed, "SettingsPage"); } }, primary: false);
            var openFolder = Theme.Button("Open data folder", () => { try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(Settings.Path)) { UseShellExecute = true }); } catch (Exception swallowed) { Diag.Ignored(swallowed, "SettingsPage"); } }, primary: false);
            openFolder.Margin = new Thickness(8, 0, 0, 0);
            files.Children.Add(openSettings);
            files.Children.Add(openFolder);
            p.Children.Add(files);
            var credits = Theme.Text($"HearthStoner {AppVersion.Current.Detail}. Card text and art: HearthstoneJSON. Meta stats: Firestone. Memory reading: UnitySpy (MIT). Fonts: Barlow (SIL Open Font License).", 12, Theme.Dusk, wrap: true);
            return AppShell.Page("Settings", "Saved as you change them.", Arrange(shell.Size, sections, credits));
        }

        /// <summary>The board art job's state, live while the page is open, and a Regenerate button. New art redraws the page.</summary>
        private static UIElement ArtStatus(AppShell shell)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var text = Theme.Text(ArtJob.Status.Text, 13.5, Theme.Dusk, wrap: true);
            text.MaxWidth = 520; text.VerticalAlignment = VerticalAlignment.Center;
            var regen = Theme.Button("Regenerate", () => GameArt.StartJob(shell.Data.Services.InstallDir, force: true), primary: false);
            regen.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(text);
            row.Children.Add(regen);
            var builds = ArtJob.Builds;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            void Update()
            {
                var now = ArtJob.Status;
                text.Text = now.Text;
                text.Foreground = now.Kind == "failed" ? Theme.Gold : Theme.Dusk;
                regen.IsEnabled = now.Kind is "idle" or "done" or "failed";
                if (ArtJob.Builds != builds) { timer.Stop(); shell.Redraw(); }
            }
            Update();
            timer.Tick += (s, e) => Update();
            row.Loaded += (s, e) => timer.Start();
            row.Unloaded += (s, e) => timer.Stop();
            return row;
        }

        /// <summary>This version, what the last update check found, and Check now / What's new / Update buttons; live while shown.</summary>
        private static UIElement UpdateRow()
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            var notes = Theme.Button("What's new", () =>
            {
                var page = AppUpdater.Available?.PageUrl;
                if (string.IsNullOrEmpty(page)) return;
                try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); } catch (Exception swallowed) { Diag.Ignored(swallowed, "SettingsPage"); }
            }, primary: false);
            var check = Theme.Button("Check now", AppUpdater.CheckNow, primary: false);
            var install = Theme.Button("Update", AppUpdater.Install);
            check.Margin = new Thickness(8, 0, 0, 0);
            install.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(notes);
            buttons.Children.Add(check);
            buttons.Children.Add(install);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Theme.Text("HearthStoner " + AppVersion.Current.Number, 14.5));
            var status = Theme.Text("", 12.5, Theme.Dusk, wrap: true);
            label.Children.Add(status);
            row.Children.Add(label);

            void Update()
            {
                var available = AppUpdater.Available;
                var busy = AppUpdater.Busy;
                status.Text = AppUpdater.Status;
                status.Foreground = available != null ? Theme.Gold : Theme.Dusk;
                notes.Visibility = install.Visibility = available != null ? Visibility.Visible : Visibility.Collapsed;
                install.Content = available != null ? $"Update to {available.Version}" : "Update";
                check.IsEnabled = install.IsEnabled = !busy;
            }
            void OnChanged() => row.Dispatcher.BeginInvoke((Action)Update);
            row.Loaded += (s, e) => { AppUpdater.Changed -= OnChanged; AppUpdater.Changed += OnChanged; Update(); };
            row.Unloaded += (s, e) => AppUpdater.Changed -= OnChanged;
            Update();
            return row;
        }

        private static string Closest(double v, params double[] options) =>
            options.OrderBy(o => Math.Abs(o - v)).First().ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A label with a keyboard shortcut in a key chip at the right.</summary>
        private static UIElement KeyRow(string label, string key)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var k = new Border { CornerRadius = new CornerRadius(5), Background = Theme.Raised, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Padding = new Thickness(8, 2, 8, 3), Child = Theme.Text(key, 12.5, Theme.Vellum, FontWeights.Medium) };
            DockPanel.SetDock(k, Dock.Right);
            row.Children.Add(k);
            row.Children.Add(Theme.Text(label, 14));
            return row;
        }

        /// <summary>Marks the sections that make the left column on wide screens (everything about the overlay and replays).</summary>
        private const string LeftColumn = "left";

        /// <summary>Starts a new section (a heading and an optional line under it), adds it to <paramref name="sections"/> and returns it to fill.</summary>
        /// <param name="left">On wide screens, put it in the left column (the rest go right).</param>
        private static StackPanel Section(List<StackPanel> sections, string title, string sub, bool left = false)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 30), Tag = left ? LeftColumn : null };
            var h = Theme.Text(title, 22, Theme.Vellum, FontWeights.SemiBold, display: true);
            h.Margin = new Thickness(0, 0, 0, sub == null ? 10 : 2);
            p.Children.Add(h);
            if (sub != null) { var s = Theme.Text(sub, 13, Theme.Dusk, wrap: true); s.Margin = new Thickness(0, 0, 0, 12); p.Children.Add(s); }
            sections.Add(p);
            return p;
        }

        /// <summary>
        /// One column (760 px at most, so toggles stay near their labels) or, on wide screens, two: the overlay and
        /// replay board on the left, everything else on the right. The credits go under both.
        /// </summary>
        private static UIElement Arrange(PageWidth size, List<StackPanel> sections, UIElement credits)
        {
            const double column = 760;
            if (size != PageWidth.Wide)
            {
                var one = new StackPanel { MaxWidth = column, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var s in sections) one.Children.Add(s);
                one.Children.Add(credits);
                return one;
            }
            var left = new StackPanel();
            var right = new StackPanel();
            foreach (var s in sections) (Equals(s.Tag, LeftColumn) ? left : right).Children.Add(s);
            right.Children.Add(credits);
            var cols = PageLayout.Columns((left, column), (right, column));
            cols.ColumnDefinitions[1].Width = new GridLength(64);   // a wider gutter between two forms
            cols.HorizontalAlignment = HorizontalAlignment.Left;
            return cols;
        }

        private static UIElement Label(string title, string sub)
        {
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(Theme.Text(title, 14.5));
            if (sub != null) s.Children.Add(Theme.Text(sub, 12.5, Theme.Dusk, wrap: true));
            return s;
        }

        private static UIElement Toggle(string title, string sub, bool value, Action<bool> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true };
            var cb = new CheckBox { IsChecked = value, Style = (Style)Theme.Styles["Toggle"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(cb, title);
            cb.Checked += (s, e) => set(true);
            cb.Unchecked += (s, e) => set(false);
            DockPanel.SetDock(cb, Dock.Right);
            row.Children.Add(cb);
            row.Children.Add(Label(title, sub));
            return row;
        }

        private static UIElement Choice(string title, string sub, (string Value, string Label)[] options, string current, Action<string> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = true };
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            var group = "seg-" + title;
            foreach (var (value, label) in options)
            {
                var rb = new RadioButton { Content = label, GroupName = group, Style = (Style)Theme.Styles["Segment"], IsChecked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) };
                var v = value;
                rb.Checked += (s, e) => { if (rb.IsLoaded) set(v); };
                seg.Children.Add(rb);
            }
            var box = new Border { CornerRadius = new CornerRadius(8), Background = Theme.Slate, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Padding = new Thickness(2), Child = seg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(box);
            row.Children.Add(Label(title, sub));
            return row;
        }
    }
}
