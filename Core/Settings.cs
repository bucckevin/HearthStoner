using System;
using System.IO;
using Newtonsoft.Json;

namespace HearthStoner
{
    /// <summary>
    /// User settings, stored in %LocalAppData%\HearthStoner\settings.json. The file is created with the defaults the
    /// first time HearthStoner runs; edit it and restart HearthStoner to apply. Command-line options override it.
    /// </summary>
    public sealed class Settings
    {
        // ---- meta stats
        /// <summary>"auto" = your current rank bracket (read from memory), else all / bronze-gold / platinum / diamond / legend / ...</summary>
        public string MetaRank { get; set; } = "auto";
        public string MetaPeriod { get; set; } = "past-7";

        // ---- overlay layout
        public double OverlayOpacity { get; set; } = 0.82;
        public double PanelWidth { get; set; } = 270;
        public double FontScale { get; set; } = 1.0;
        /// <summary>
        /// Where the panels go: "right" (opponent and you side by side at the right edge of the game window),
        /// "left" (both at the left edge), or "split" (opponent on the left edge, you on the right edge).
        /// </summary>
        public string Layout { get; set; } = "right";
        /// <summary>The replay's board: one rendered from the game by Art/GameArtBuilder, by its root name (e.g. "ULD").</summary>
        public string ReplayBoard { get; set; } = "ULD";
        /// <summary>Swap the two panels: your info first, opponent second.</summary>
        public bool SwapSides { get; set; }
        public bool CardArt { get; set; } = true;
        /// <summary>In the deck list, show each card's chance to be your next draw next to its count.</summary>
        public bool DeckShowOdds { get; set; } = true;

        // ---- which panels show
        public bool ShowHealth { get; set; } = true;
        public bool ShowOpponent { get; set; } = true;
        public bool ShowOpponentPlayed { get; set; } = true;
        public bool ShowSecrets { get; set; } = true;
        public bool ShowMulligan { get; set; } = true;
        public bool ShowThisTurn { get; set; } = true;
        public bool ShowBoard { get; set; } = true;
        public bool ShowDeck { get; set; } = true;
        /// <summary>Marks over the opponent's hand: the turn each card arrived, mulligan keeps, created/returned cards.</summary>
        public bool ShowOpponentHandMarks { get; set; } = true;
        public bool ShowDiscover { get; set; } = true;
        public bool ShowLastGameReview { get; set; } = true;

        // ---- hotkeys (work while Hearthstone is focused). Format: "Ctrl+Shift+H".
        public string HotkeyToggleOverlay { get; set; } = "Ctrl+Shift+H";
        public string HotkeyToggleDeck { get; set; } = "Ctrl+Shift+D";
        public string HotkeyToggleOpponent { get; set; } = "Ctrl+Shift+O";
        public string HotkeyToggleAdvice { get; set; } = "Ctrl+Shift+A";
        /// <summary>Hover mode: the overlay takes the mouse so you can hover cards for details (press again to give it back).</summary>
        public string HotkeyHoverMode { get; set; } = "Ctrl+Shift+M";

        // ---- app
        public bool StartWithWindows { get; set; }
        /// <summary>Deck codes you pasted into the Decks page, to analyze like your own decks.</summary>
        public System.Collections.Generic.List<ImportedDeck> ImportedDecks { get; set; } = new System.Collections.Generic.List<ImportedDeck>();
        /// <summary>Battle.net.exe or Hearthstone.exe to start the game with; empty = find Battle.net automatically.</summary>
        public string LaunchPath { get; set; }
        /// <summary>Your BattleTag, remembered from the last time Hearthstone was running (shown in the app).</summary>
        public string LastBattleTag { get; set; }
        public bool CheckForUpdates { get; set; } = true;

        // ---- app window
        /// <summary>The whole app window's zoom (text and everything else): 0.9 to 1.5, 1 = normal.</summary>
        public double AppScale { get; set; } = 1.0;
        /// <summary>Open the window where it was last closed, at the same size (and maximized if it was).</summary>
        public bool RememberWindow { get; set; } = true;
        /// <summary>The window's last place and size when not maximized, in screen units; 0 width = never saved.</summary>
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        /// <summary>The zoom steps offered in Settings.</summary>
        public static readonly double[] AppScales = { 0.9, 1.0, 1.15, 1.3, 1.5 };

        /// <summary><paramref name="scale"/> kept to the offered range (a hand-edited settings file can hold anything).</summary>
        public static double ClampScale(double scale) =>
            double.IsNaN(scale) || scale <= 0 ? 1.0 : Math.Max(AppScales[0], Math.Min(AppScales[AppScales.Length - 1], scale));

        public static string Path =>
            AppPaths.Data("settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var s = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(Path)) ?? new Settings();
                    s.Save();   // writes back any settings added since the file was created
                    return s;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[settings] {Path} couldn't be read ({ex.Message}); using defaults.");
                return new Settings();
            }
            var d = new Settings();
            d.Save();
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception swallowed) { Diag.Ignored(swallowed, "Settings"); /* settings are best-effort */ }
        }
    }

    public sealed class ImportedDeck
    {
        public string Name { get; set; }
        public string Code { get; set; }
    }

    /// <summary>Parses "Ctrl+Shift+H" style hotkeys into RegisterHotKey modifiers and a virtual key.</summary>
    public static class Hotkeys
    {
        public static bool Parse(string combo, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(combo)) return false;
            foreach (var raw in combo.Split('+'))
            {
                var p = raw.Trim().ToUpperInvariant();
                if (p == "CTRL" || p == "CONTROL") mods |= 0x2;
                else if (p == "SHIFT") mods |= 0x4;
                else if (p == "ALT") mods |= 0x1;
                else if (p == "WIN") mods |= 0x8;
                else if (p.Length == 1 && char.IsLetterOrDigit(p[0])) vk = p[0];
                else if (p.Length >= 2 && p[0] == 'F' && int.TryParse(p.Substring(1), out var f) && f >= 1 && f <= 24) vk = (uint)(0x70 + f - 1);
                else return false;
            }
            return vk != 0;
        }
    }
}
