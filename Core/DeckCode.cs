using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HackF5.UnitySpy.HearthstoneLib.Detail.Deck;

namespace HearthStoner
{
    /// <summary>
    /// Builds Hearthstone deck codes (the "AAECA..." strings you can paste into the game).
    /// Format: base64 of varints: 0, version 1, format, [heroes], [1-copy cards], [2-copy cards], [n-copy (id, n)].
    /// Sideboards (e.g. E.T.C.) are left out; the main deck still imports.
    /// </summary>
    public static class DeckCode
    {
        /// <summary>The class of a deck code's hero (e.g. "PALADIN"), or null.</summary>
        public static string HeroClass(string code, CardDb cards)
        {
            try
            {
                var bytes = Convert.FromBase64String(code.Trim());
                var pos = 0;
                int R()
                {
                    int v = 0, shift = 0;
                    while (pos < bytes.Length)
                    {
                        var b = bytes[pos++];
                        v |= (b & 0x7f) << shift;
                        if (b < 0x80) return v;
                        shift += 7;
                    }
                    throw new FormatException();
                }
                R(); R(); R();
                return R() > 0 ? cards.ByDbf(R())?.CardClass : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Card ids (one entry per copy) in the deck you actually start the game with, or null if the code can't be
        /// read. Includes cards that sideboard picks add to the deck: Commander Beatrix's "Ten copies join your deck!"
        /// adds 10 copies of her pick. Picks that stay out of the deck (E.T.C.'s band, King of the Underbelly's beasts,
        /// Zilliax's modules) are not included.
        /// </summary>
        public static List<string> Decode(string code, CardDb cards)
        {
            var d = DecodeFull(code, cards);
            if (d == null) return null;
            var result = d.Main.ToList();
            result.AddRange(JoiningCopies(d.Sideboards, cards));
            return result;
        }

        public sealed class Sideboard { public string CardId; public string OwnerId; public int Count; }
        public sealed class Decoded { public List<string> Main = new List<string>(); public List<Sideboard> Sideboards = new List<Sideboard>(); }

        /// <summary>Copies that sideboard picks add to the starting deck (cards whose text says "N copies join your deck").</summary>
        public static IEnumerable<string> JoiningCopies(IEnumerable<Sideboard> sideboards, CardDb cards)
        {
            foreach (var sb in sideboards)
            {
                var owner = cards.ById(sb.OwnerId);
                var n = CopiesJoining(owner?.Text);
                for (var i = 0; i < n * Math.Max(1, sb.Count); i++) yield return sb.CardId;
            }
        }

        private static readonly string[] Numbers = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };

        /// <summary>"... Ten copies join your deck!" -> 10. 0 when the card doesn't add copies.</summary>
        public static int CopiesJoining(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var t = CardProfile.Clean(text).ToLowerInvariant();
            var m = System.Text.RegularExpressions.Regex.Match(t, @"(\w+)\s+cop(?:y|ies) joins? your deck");
            if (!m.Success) return 0;
            var w = m.Groups[1].Value;
            if (int.TryParse(w, out var n)) return n;
            var idx = Array.IndexOf(Numbers, w);
            return idx > 0 ? idx : 0;
        }

        public static Decoded DecodeFull(string code, CardDb cards)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(code.Trim()); }
            catch { return null; }
            var pos = 0;
            int R()
            {
                int v = 0, shift = 0;
                while (pos < bytes.Length)
                {
                    var b = bytes[pos++];
                    v |= (b & 0x7f) << shift;
                    if (b < 0x80) return v;
                    shift += 7;
                }
                throw new FormatException("truncated deck code");
            }
            string Id(int dbf) => cards.ByDbf(dbf)?.Id ?? throw new FormatException("unknown card " + dbf);
            try
            {
                var d = new Decoded();
                R(); R(); R();                              // reserved, version, format
                var heroes = R(); for (var i = 0; i < heroes; i++) R();
                var ones = R(); for (var i = 0; i < ones; i++) d.Main.Add(Id(R()));
                var twos = R(); for (var i = 0; i < twos; i++) { var id = Id(R()); d.Main.Add(id); d.Main.Add(id); }
                var many = R(); for (var i = 0; i < many; i++) { var id = Id(R()); var n = R(); for (var j = 0; j < n; j++) d.Main.Add(id); }

                // Optional sideboards: flag, then (card, owner) for 1 copy, (card, owner) for 2, (card, n, owner).
                if (pos < bytes.Length && R() == 1)
                {
                    var s1 = R(); for (var i = 0; i < s1; i++) { var c = Id(R()); d.Sideboards.Add(new Sideboard { CardId = c, OwnerId = Id(R()), Count = 1 }); }
                    var s2 = R(); for (var i = 0; i < s2; i++) { var c = Id(R()); d.Sideboards.Add(new Sideboard { CardId = c, OwnerId = Id(R()), Count = 2 }); }
                    var sn = R(); for (var i = 0; i < sn; i++) { var c = Id(R()); var n = R(); d.Sideboards.Add(new Sideboard { CardId = c, Count = n, OwnerId = Id(R()) }); }
                }
                return d;
            }
            catch { return null; }
        }

        /// <summary>
        /// Finds the deck code in pasted text: either a bare code, or Hearthstone's "copy deck" block
        /// ("### Name", "# 2x (1) Card", the code, "# To use this deck..."). Returns null if there's no valid code.
        /// </summary>
        public static string FindCode(string text, CardDb cards, out string name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(text)) return null;
            string code = null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("###")) { name = line.TrimStart('#').Trim(); continue; }
                if (line.Length == 0 || line.StartsWith("#")) continue;
                foreach (var word in line.Split(' ', '\t'))
                    if (word.Length >= 20 && DecodeFull(word, cards) is Decoded d && d.Main.Count > 0) { code = word; break; }
                if (code != null) break;
            }
            return code;
        }

        public static string Encode(Deck deck, CardDb cards)
        {
            if (deck?.DeckList == null || deck.DeckList.Count == 0) return null;
            var hero = cards.ById(deck.HeroCardId);
            if (hero == null || hero.DbfId == 0) return null;

            var counts = new Dictionary<int, int>();
            foreach (var id in deck.DeckList)
            {
                var c = cards.ById(id);
                if (c == null || c.DbfId == 0) return null;   // unknown card: don't emit a broken code
                counts[c.DbfId] = counts.TryGetValue(c.DbfId, out var n) ? n + 1 : 1;
            }

            using (var ms = new MemoryStream())
            {
                void W(int v)
                {
                    var u = (uint)v;
                    while (u >= 0x80) { ms.WriteByte((byte)(u | 0x80)); u >>= 7; }
                    ms.WriteByte((byte)u);
                }

                W(0);
                W(1);
                W(deck.FormatType > 0 ? deck.FormatType : 2);
                W(1); W(hero.DbfId);

                var ones = counts.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(x => x).ToList();
                var twos = counts.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(x => x).ToList();
                var many = counts.Where(kv => kv.Value > 2).OrderBy(kv => kv.Key).ToList();

                W(ones.Count); foreach (var x in ones) W(x);
                W(twos.Count); foreach (var x in twos) W(x);
                W(many.Count); foreach (var kv in many) { W(kv.Key); W(kv.Value); }

                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }
}
