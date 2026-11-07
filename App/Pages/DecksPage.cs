using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HearthStoner
{
    /// <summary>Decks: pick a deck on the left; see its list, the swaps the data favours and the best list of its archetype.</summary>
    public static class DecksPage
    {
        public static UIElement Build(AppShell shell)
        {
            var d = shell.Data;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var list = new StackPanel();
            var head = new StackPanel { Margin = new Thickness(22, 24, 22, 16) };
            head.Children.Add(Theme.Title("Decks", 32));
            var own = d.Decks.Count(x => !x.Imported);
            var imported = d.Decks.Count - own;
            head.Children.Add(Theme.Text($"{own} saved deck{(own == 1 ? "" : "s")}{(imported > 0 ? $", {imported} pasted in" : "")}", 13, Theme.Dusk));
            list.Children.Add(head);
            list.Children.Add(ImportBox(shell));
            if (shell.SelectedDeck == int.MaxValue) shell.SelectedDeck = d.Decks.Count - 1;   // a deck just pasted in
            if (shell.SelectedDeck < 0 || shell.SelectedDeck >= d.Decks.Count) shell.SelectedDeck = 0;
            for (var i = 0; i < d.Decks.Count; i++) list.Children.Add(DeckRow(d.Decks[i], i, shell));
            g.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false } });

            UIElement detail = d.Decks.Count == 0
                ? PlayPage.Empty("No decks yet", "Open Hearthstone once. It writes your saved decks to its log, and they'll show up here.")
                : Detail(d.Decks[shell.SelectedDeck], shell);
            var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(30, 24, 26, 26), Focusable = false };
            Grid.SetColumn(scroll, 1);
            g.Children.Add(scroll);
            return g;
        }

        private static UIElement DeckRow(PlayerDeck deck, int index, AppShell shell)
        {
            var pick = shell.Data.PickFor(deck);
            var selected = index == shell.SelectedDeck;
            var row = new DockPanel { Margin = new Thickness(22, 10, 22, 10) };
            var wr = pick?.Archetype != null ? Theme.Text(AppShell.Pct(pick.Expected, 1), 18, Theme.Vellum, FontWeights.SemiBold, display: true) : Theme.Text("-", 15, Theme.Dusk);
            wr.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(wr, Dock.Right);
            row.Children.Add(wr);
            row.Children.Add(new Border { Width = 4, Height = 30, CornerRadius = new CornerRadius(2), Background = Theme.B(Theme.ClassColor(deck.Class)), Margin = new Thickness(0, 0, 12, 0) });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Theme.Text(deck.Name, 14.5, Theme.Vellum, FontWeights.SemiBold));
            names.Children.Add(Theme.Text((deck.Imported ? "Pasted in, " : "") + (pick?.Archetype?.Name ?? MetaStats.ClassName(deck.Class)), 12.5, deck.Imported ? Theme.Mana : Theme.Dusk));
            row.Children.Add(names);
            var b = new Border
            {
                Child = row, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? new LinearGradientBrush(Color.FromArgb(31, 90, 160, 255), Colors.Transparent, 0) : (Brush)Brushes.Transparent,
            };
            if (selected)
            {
                var wrap = new Grid();
                wrap.Children.Add(b);
                wrap.Children.Add(new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.Mana });
                return wrap;
            }
            b.MouseLeftButtonUp += (s, e) => { shell.SelectedDeck = index; shell.Redraw(); };
            return b;
        }

        /// <summary>A box to paste a deck code (or Hearthstone's whole "copy deck" text) into.</summary>
        private static UIElement ImportBox(AppShell shell)
        {
            var p = new StackPanel { Margin = new Thickness(22, 0, 22, 14) };
            var box = new TextBox
            {
                Height = 34, Padding = new Thickness(8, 6, 8, 6), FontSize = 13, FontFamily = Theme.Body,
                Background = Theme.Slate, Foreground = Theme.Vellum, BorderBrush = Theme.Line, CaretBrush = Theme.Vellum,
                ToolTip = "Paste a deck code, or the whole text Hearthstone copies",
            };
            var hint = Theme.Text("Paste a deck code to analyze", 13, Theme.Dusk);
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(11, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            box.TextChanged += (s, e) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var field = new Grid();
            field.Children.Add(box);
            field.Children.Add(hint);
            p.Children.Add(field);
            var add = Theme.Button("Analyze deck", () =>
            {
                var cards = shell.Data.Cards;
                var code = DeckCode.FindCode(box.Text, cards, out var name);
                if (code == null) { shell.Status?.Invoke("That doesn't contain a deck code Hearthstone recognises. Copy the deck in Hearthstone or from a deck site and paste it here."); return; }
                var st = shell.Data.Services.Settings;
                if (st.ImportedDecks.Any(x => x.Code == code) || shell.Data.Decks.Any(x => x.Code == code)) { shell.Status?.Invoke("That deck is already in your list."); return; }
                var count = st.ImportedDecks.Count + 1;
                st.ImportedDecks.Add(new ImportedDeck { Name = string.IsNullOrWhiteSpace(name) ? $"Pasted deck {count}" : name, Code = code });
                try { st.Save(); } catch (Exception swallowed) { Diag.Ignored(swallowed, "DecksPage"); }
                shell.SelectedDeck = int.MaxValue;   // select it once the data reloads (clamped to the last deck)
                shell.ReloadRequested?.Invoke();
            }, primary: false);
            add.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(add);
            return p;
        }

        private static void RemoveImported(PlayerDeck deck, AppShell shell)
        {
            var st = shell.Data.Services.Settings;
            st.ImportedDecks.RemoveAll(x => x.Code == deck.Code);
            try { st.Save(); } catch (Exception swallowed) { Diag.Ignored(swallowed, "DecksPage"); }
            shell.SelectedDeck = 0;
            shell.ReloadRequested?.Invoke();
        }

        private static UIElement Detail(PlayerDeck deck, AppShell shell)
        {
            var d = shell.Data;
            var pick = d.PickFor(deck);
            var plan = d.Planner?.Tune(deck);
            var p = new StackPanel();

            // Banner: signature art, name, archetype, numbers, copy code.
            var into = Theme.SlateC;
            var banner = new Grid { Height = 150 };
            banner.Children.Add(AppShell.ArtBanner(pick?.SignatureCardId, into, 420, 0.9, 0.0, 0.65));
            banner.Children.Add(new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.B(Theme.ClassColor(deck.Class)) });
            var info = new Grid { Margin = new Thickness(26, 20, 20, 20) };
            var titles = new StackPanel();
            titles.Children.Add(Theme.Text(deck.Name, 30, Theme.Vellum, FontWeights.SemiBold, display: true));
            titles.Children.Add(Theme.Text($"{pick?.Archetype?.Name ?? MetaStats.ClassName(deck.Class)}, {deck.Cards.Count} cards", 13.5, Theme.Dusk));
            info.Children.Add(titles);
            var nums = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            void Num(string v, string label, Brush b) { var s = new StackPanel { Margin = new Thickness(0, 0, 30, 0) }; s.Children.Add(Theme.Text(v, 26, b, FontWeights.SemiBold, display: true)); s.Children.Add(Theme.Text(label, 12, Theme.Dusk)); nums.Children.Add(s); }
            if (pick?.Archetype != null)
            {
                Num(AppShell.Pct(pick.Expected, 1), "expected today", Theme.Vellum);
                Num(AppShell.Pct(pick.Archetype.WinRate, 1), "archetype overall", Theme.Vellum);
                Num($"{pick.Archetype.Games:N0}", "games in the data", Theme.Vellum);
            }
            info.Children.Add(nums);
            var bannerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            if (deck.Imported)
            {
                var remove = Theme.Button("Remove", () => RemoveImported(deck, shell), primary: false);
                remove.Margin = new Thickness(0, 0, 8, 0);
                bannerActions.Children.Add(remove);
            }
            bannerActions.Children.Add(PlayPage.CopyButton(deck.Code, d));
            info.Children.Add(bannerActions);
            banner.Children.Add(info);
            p.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Theme.Slate, ClipToBounds = true, Child = banner, Margin = new Thickness(0, 0, 0, 24) });

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var listCol = new StackPanel();
            listCol.Children.Add(Theme.Heading("Card list"));
            foreach (var c in deck.Cards.GroupBy(x => x).Select(gr => new { Id = gr.Key, Info = d.Cards.ById(gr.Key), N = gr.Count() })
                         .OrderBy(x => x.Info?.Cost ?? 99).ThenBy(x => x.Info?.Name))
            {
                var badge = c.Info?.Rarity == "LEGENDARY" && c.N == 1 ? "★" : c.N > 1 ? c.N.ToString() : null;
                listCol.Children.Add(SectionView.CardTile(AdviceLine.Card(c.Info, c.Id, $"{c.N}x {c.Info?.Name}", Tone.Normal, badge, null), true, true));
            }
            cols.Children.Add(listCol);

            var right = new StackPanel();
            Grid.SetColumn(right, 2);
            if (!deck.Imported)
            {
                var versions = DeckVersions.For(deck, d.Decks, d.Games, d.Cards);
                if (versions.Versions.Any(v => v.Games > 0)) right.Children.Add(Versions(versions, d));
            }
            right.Children.Add(Theme.Heading("Swaps the data favours"));
            if (plan == null)
                right.Children.Add(Theme.Text("This deck doesn't match a meta archetype, so there's nothing to compare it with yet.", 13.5, Theme.Dusk, wrap: true));
            else
            {
                if (plan.Swaps.Count == 0) right.Children.Add(Theme.Text("Your list lines up with what wins in this archetype.", 13.5, Theme.Win, wrap: true));
                foreach (var sw in plan.Swaps) right.Children.Add(SwapRow(sw, plan, d));
                var note = Theme.Text($"Win rate in games where the card was drawn, against the archetype's {AppShell.Pct(plan.Baseline, 1)}.", 12, Theme.Dusk, wrap: true);
                note.Margin = new Thickness(0, 4, 0, 0);
                right.Children.Add(note);
                if (plan.Best != null) right.Children.Add(BestList(plan.Best, d));
            }
            cols.Children.Add(right);
            p.Children.Add(cols);
            return p;
        }

        /// <summary>Each list you've played this deck with, newest first: its record and what changed from the one before.</summary>
        private static UIElement Versions(DeckVersions v, AppData d)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
            p.Children.Add(Theme.Heading(v.Versions.Count == 1 ? "Your results with this list" : $"Versions ({v.Versions.Count})"));
            string Cards(Dictionary<string, int> m) => string.Join(", ", m.OrderBy(kv => d.Cards.NameOf(kv.Key)).Select(kv => (kv.Value > 1 ? $"{kv.Value}x " : "") + d.Cards.NameOf(kv.Key)));
            foreach (var ver in Enumerable.Reverse(v.Versions))
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel();
                var title = new TextBlock { FontSize = 14, Foreground = Theme.Vellum, FontWeight = FontWeights.SemiBold };
                title.Inlines.Add(new System.Windows.Documents.Run(v.Versions.Count == 1 ? "This list" : $"Version {ver.Number}"));
                if (ver.IsCurrent && v.Versions.Count > 1) title.Inlines.Add(new System.Windows.Documents.Run("  current") { Foreground = Theme.Mana, FontSize = 12 });
                left.Children.Add(title);
                var when = ver.First == null ? "Not played yet" : ver.First.Value.Date == ver.Last.Value.Date ? ver.First.Value.ToString("MMM d") : $"{ver.First.Value:MMM d} to {ver.Last.Value:MMM d}";
                left.Children.Add(Theme.Text(when, 12, Theme.Dusk));
                if (ver.Added.Count > 0) left.Children.Add(Theme.Text("+ " + Cards(ver.Added), 12.5, Theme.Win, wrap: true));
                if (ver.Removed.Count > 0) left.Children.Add(Theme.Text("- " + Cards(ver.Removed), 12.5, Theme.Loss, wrap: true));
                g.Children.Add(left);
                var rec = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 0, 0) };
                if (ver.Games > 0)
                {
                    var wr = Theme.Text(AppShell.Pct(ver.WinRate), 20, Theme.RateBrush(ver.WinRate), FontWeights.SemiBold, display: true);
                    wr.TextAlignment = TextAlignment.Right;
                    rec.Children.Add(wr);
                    var wl = Theme.Text($"{ver.Wins}-{ver.Losses}", 12, Theme.Dusk);
                    wl.TextAlignment = TextAlignment.Right;
                    rec.Children.Add(wl);
                }
                Grid.SetColumn(rec, 1);
                g.Children.Add(rec);
                p.Children.Add(new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 8), Child = g });
            }
            var few = v.Versions.Where(x => x.Games > 0).Sum(x => x.Games) < 20;
            var note = Theme.Text(few ? "From your recorded games. A handful of games per version is mostly luck; the gap means more after 20 or so." : "From your recorded games.", 12, Theme.Dusk, wrap: true);
            p.Children.Add(note);
            return p;
        }

        private static UIElement SwapRow(DeckPlanner.Swap sw, DeckPlanner.TuningPlan plan, AppData d)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement Side(string id, double wr, string extra, bool cut)
            {
                var s = new StackPanel();
                if (id == null) { s.Children.Add(Theme.Text(cut ? "Nothing to cut" : "", 12.5, Theme.Dusk)); return s; }
                var info = d.Cards.ById(id);
                var tile = SectionView.CardTile(AdviceLine.Card(info, id, info?.Name, Tone.Normal, null, null, struck: false), true, true);
                if (cut) ((UIElement)tile).Opacity = 0.75;
                s.Children.Add(tile);
                var t = new TextBlock { FontSize = 12.5, Foreground = Theme.Dusk, Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
                t.Inlines.Add(new System.Windows.Documents.Run(AppShell.Pct(wr)) { Foreground = Theme.RateBrush(wr - plan.Baseline + 0.5), FontWeight = FontWeights.SemiBold });
                t.Inlines.Add(new System.Windows.Documents.Run(" when drawn" + extra));
                s.Children.Add(t);
                return s;
            }
            var inExtra = sw.InId == null ? "" : $", in {AppShell.Pct(sw.InPlayRate)} of lists" +
                (sw.InOwned > 0 ? ", owned" : sw.InOwned == 0 && sw.InCraftCost > 0 ? $", {sw.InCraftCost} dust" : "");
            g.Children.Add(Side(sw.OutId, sw.OutWinRate, "", true));
            var arrow = Theme.Text("→", 18, Theme.Dusk);
            arrow.HorizontalAlignment = HorizontalAlignment.Center;
            arrow.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(arrow, 1);
            g.Children.Add(arrow);
            var right = Side(sw.InId, sw.InWinRate, inExtra, false);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return g;
        }

        private static UIElement BestList(DeckPlanner.BestList b, AppData d)
        {
            var p = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            p.Children.Add(Theme.Heading("The best list of this archetype"));
            var line = Theme.Text($"{AppShell.Pct(b.WinRate, 1)} over {b.Games:N0} games. {b.Add.Count} card{(b.Add.Count == 1 ? "" : "s")} differ from yours" +
                (b.Dust > 0 ? $"; {b.Dust:N0} dust to build it from your collection." : b.Dust == 0 ? "; you own every card." : "."), 13.5, Theme.Dusk, wrap: true);
            line.Margin = new Thickness(0, -4, 0, 10);
            p.Children.Add(line);
            string Group(List<string> ids) => string.Join(", ", ids.GroupBy(x => x).Select(gr => (gr.Count() > 1 ? $"{gr.Count()}x " : "") + d.Cards.NameOf(gr.Key)));
            if (b.Add.Count > 0) p.Children.Add(Diff("Add", Group(b.Add), Theme.Win));
            if (b.Remove.Count > 0) p.Children.Add(Diff("Cut", Group(b.Remove), Theme.Loss));
            var copy = PlayPage.CopyButton(b.Code, d, primary: false);
            ((Button)copy).Content = "Copy the best list";
            copy.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(copy);
            return p;
        }

        private static UIElement Diff(string label, string cards, Brush color)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Theme.Text(label, 13.5, color, FontWeights.SemiBold));
            var t = Theme.Text(cards, 13.5, Theme.Vellum, wrap: true);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return g;
        }
    }
}
