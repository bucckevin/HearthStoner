using System;
using System.Collections.Generic;
using System.Linq;

namespace HearthStoner
{
    public enum HandMarkKind { Drawn, Kept, Mulliganed, Coin, Created, Returned, Stolen }

    /// <summary>What the overlay shows above one card in the opponent's hand (all public information).</summary>
    public sealed class HandMark
    {
        /// <summary>0-based, left to right as you see their hand (ZONE_POSITION - 1).</summary>
        public int Slot;
        /// <summary>Their turn number when the card arrived (0 = opening hand / mulligan).</summary>
        public int Turn;
        public HandMarkKind Kind;
        /// <summary>The card, when it's public (returned to hand, the Coin, some created cards).</summary>
        public string CardId;
        public string CardName;
        /// <summary>Short extra text: the card that created it, etc.</summary>
        public string Note;
    }

    /// <summary>
    /// Where each card of the opponent's hand sits on screen. Hearthstone draws its board as a 4:3 area centred in
    /// the window (wider windows add space at the sides), and the fanned hand at the top depends on how many cards
    /// it holds. Positions are fractions of that 4:3 area, one row per hand size (1 to 10 cards), from the
    /// measurements Hearthstone Deck Tracker uses for its card marks (taken at 1024x768).
    /// </summary>
    public static class HandGeometry
    {
        private static readonly int[][] Px =
        {
            new[] { 480 },
            new[] { 439, 520 },
            new[] { 392, 479, 569 },
            new[] { 382, 446, 512, 580 },
            new[] { 375, 427, 479, 533, 586 },
            new[] { 371, 414, 458, 502, 546, 591 },
            new[] { 368, 405, 442, 479, 517, 555, 594 },
            new[] { 365, 397, 430, 462, 496, 530, 563, 597 },
            new[] { 363, 392, 421, 450, 479, 508, 539, 569, 599 },
            new[] { 364, 388, 414, 440, 467, 492, 520, 546, 573, 600 },
        };
        private static readonly int[][] Py =
        {
            new[] { 48 },
            new[] { 47, 48 },
            new[] { 33, 47, 40 },
            new[] { 21, 41, 47, 43 },
            new[] { 23, 39, 47, 46, 36 },
            new[] { 12, 30, 43, 48, 47, 39 },
            new[] { 15, 31, 41, 48, 47, 41, 31 },
            new[] { 4, 22, 35, 45, 48, 48, 43, 33 },
            new[] { 7, 23, 35, 43, 48, 47, 43, 35, 23 },
            new[] { 4, 13, 28, 38, 45, 48, 48, 44, 37, 27 },
        };

        /// <summary>
        /// Centre of card <paramref name="index"/> (0-based, left to right) in a hand of <paramref name="handCount"/>
        /// cards, in the same units as <paramref name="width"/> and <paramref name="height"/> (the game's client area).
        /// </summary>
        public static (double X, double Y) Position(int handCount, int index, double width, double height)
        {
            var n = Math.Max(1, Math.Min(10, handCount));
            var i = Math.Max(0, Math.Min(n - 1, index));
            // The 4:3 board as a share of the window's width (1 at 4:3, 0.75 at 16:9).
            var board = (4.0 / 3.0) / (width / height);
            var x = width * board * (Px[n - 1][i] / 1024.0) + width * (1 - board) / 2;
            var y = height * (Py[n - 1][i] / 768.0);
            return (x, y);
        }
    }

    public sealed partial class Advisor
    {
        /// <summary>A mark for each card in the opponent's hand, in screen order. Empty during the mulligan.</summary>
        public List<HandMark> OpponentHandMarks()
        {
            lock (_game.Sync) return OpponentHandMarksLocked();
        }

        private List<HandMark> OpponentHandMarksLocked()
        {
            var result = new List<HandMark>();
            var opp = _game.OpponentPlayerId;
            if (!_game.InGame || opp == 0 || _game.InMulligan || !GameModes.IsCardGame(_game.GameType)) return result;
            var hand = _game.Hand(opp);
            for (var i = 0; i < hand.Count; i++)
            {
                var e = hand[i];
                var slot = e.Int("ZONE_POSITION") > 0 ? e.Int("ZONE_POSITION") - 1 : i;
                var m = new HandMark { Slot = slot, Turn = Math.Max(0, (e.ZoneTurn + 1) / 2) };
                var known = !string.IsNullOrEmpty(e.CardId) || e.ReturnedToHand ? e.KnownCardId : null;
                var creator = e.Int("CREATOR");
                if (_cards.IsCoin(known) || e.SetupCoin) m.Kind = HandMarkKind.Coin;
                else if (e.ReturnedToHand) m.Kind = HandMarkKind.Returned;
                else if (creator != 0 || e.Int("CREATOR_DBID") != 0)
                {
                    m.Kind = HandMarkKind.Created;
                    var creatorId = _game.EntityCardId(creator) ?? _cards.ByDbf(e.Int("CREATOR_DBID"))?.Id;
                    m.Note = creatorId == null ? null : _cards.NameOf(creatorId);
                }
                else if (e.StartedInDeck && e.StartController == _game.FriendlyPlayerId) m.Kind = HandMarkKind.Stolen;
                else if (e.KeptInMulligan) m.Kind = HandMarkKind.Kept;
                else if (e.MulliganReplacement) m.Kind = HandMarkKind.Mulliganed;
                else m.Kind = HandMarkKind.Drawn;
                if (m.Kind == HandMarkKind.Kept || m.Kind == HandMarkKind.Mulliganed) m.Turn = 0;
                if (!string.IsNullOrEmpty(known)) { m.CardId = known; m.CardName = _cards.NameOf(known); }
                result.Add(m);
            }
            return result.OrderBy(m => m.Slot).ToList();
        }
    }
}
