using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HearthStoner.Stats
{
    /// <summary>
    /// 'HearthStoner selftest': known-answer tests for the pure logic (card profiles, deck codes, secrets, hotkeys,
    /// ladder estimate), then every Power.log in the Hearthstone Logs folder replayed through the real tracker,
    /// advisor and recorder with sanity checks per game. Prints PASS/FAIL lines; returns the number of failures.
    /// </summary>
    public static class SelfTest
    {
        private static int _fail, _pass;
        private static Action<string> _out;

        public static int Run(CardDb cards, string installDir, MetaStats meta, Action<string> output)
        {
            _fail = 0; _pass = 0; _out = output;

            Section("Card profiles");
            Profile(cards, "CORE_CS2_093", "Consecration", p => p.AoeDamage == 2 && p.AoeHitsFace);
            Profile(cards, "CORE_CS2_029", "Fireball", p => p.Damage == 6 && p.DamageHitsFace && !p.IsAoe);
            Profile(cards, "CORE_EX1_246", "Hex", p => p.Transform);
            Profile(cards, "CORE_EX1_312", "Twisting Nether", p => p.BoardClear);
            Profile(cards, "CORE_CS2_032", "Flamestrike", p => p.AoeDamage == 5 && !p.AoeHitsFace);
            Profile(cards, "CORE_CS2_024", "Frostbolt", p => p.Damage == 3 && p.DamageHitsFace && p.Freeze);
            Profile(cards, "CORE_CS2_089", "Holy Light", p => p.Heal == 8);
            Profile(cards, "CORE_CS2_023", "Arcane Intellect", p => p.Draws);
            Profile(cards, "EX1_332", "Silence", p => p.Silence);
            Profile(cards, "CORE_CS2_012", "Swipe", p => p.Damage == 4 && p.DamageHitsFace);
            var std = cards.All.Where(c => c.Collectible && c.IsStandard && c.Type != "HERO_SKIN").ToList();
            Check($"Standard pool loaded ({std.Count} cards)", std.Count > 900);
            var profiled = std.Count(c => { var p = c.Profile; return p.IsAoe || p.IsSingleRemoval || p.Heal > 0 || p.Armor > 0 || p.Draws || p.Silence || p.Freeze; });
            Info($"{profiled} of {std.Count} Standard cards have at least one role");

            Section("Secrets");
            foreach (var s in Secrets.Standard)
            {
                var c = cards.ById(s.CardId);
                Check($"{s.Name} ({s.CardId}) is a Standard secret of {s.Class}", c != null && c.Has("SECRET") && c.IsStandard && c.CardClass == s.Class && c.Name == s.Name);
            }
            var stdSecrets = std.Where(c => c.Has("SECRET")).Select(c => c.Id).ToList();
            var missing = stdSecrets.Where(id => Secrets.ById(id) == null).ToList();
            Check($"every Standard secret has a rule{(missing.Count > 0 ? " - missing: " + string.Join(", ", missing.Select(cards.NameOf)) : "")}", missing.Count == 0);

            Section("Deck codes");
            var saved = DecksLog.SavedDecks(installDir);
            Info($"{saved.Count} saved decks in Decks.log");
            foreach (var d in saved)
            {
                var list = DeckCode.Decode(d.DeckCode, cards);
                if (list == null) { Check($"decode {d.Name}", false); continue; }
                if (list.Count == 0) { Info($"{d.Name}: empty deck"); continue; }
                var heroClass = DeckCode.HeroClass(d.DeckCode, cards);
                var hero = cards.All.FirstOrDefault(c => c.Type == "HERO" && c.CardClass == heroClass && c.Collectible == false && c.Id.StartsWith("HERO_"));
                var re = DeckCode.Encode(new HackF5.UnitySpy.HearthstoneLib.Detail.Deck.Deck { DeckList = list, HeroCardId = hero?.Id ?? "HERO_04", FormatType = 2 }, cards);
                var back = re == null ? null : DeckCode.Decode(re, cards);
                Check($"{d.Name}: {list.Count} cards, {heroClass}, re-encodes to the same list",
                    back != null && back.OrderBy(x => x).SequenceEqual(list.OrderBy(x => x)) && heroClass != null);
            }

            Check("\"Ten copies join your deck!\" = 10", DeckCode.CopiesJoining("<b>Taunt</b> While building your deck, pick a 2-Cost minion. Ten copies join your deck!") == 10);
            Check("E.T.C. adds nothing to the deck", DeckCode.CopiesJoining(cards.ById("ETC_080")?.Text) == 0);
            foreach (var d in saved)
            {
                var full = DeckCode.DecodeFull(d.DeckCode, cards);
                if (full == null || full.Sideboards.Count == 0) continue;
                var list = DeckCode.Decode(d.DeckCode, cards);
                var picks = string.Join(", ", full.Sideboards.Select(x => $"{cards.NameOf(x.CardId)} (for {cards.NameOf(x.OwnerId)})"));
                var beatrix = full.Sideboards.Where(x => x.OwnerId == "JAIL_397").ToList();
                if (beatrix.Count > 0)
                    Check($"{d.Name}: Beatrix's pick {picks} adds 10 copies ({list.Count} cards)", list.Count == full.Main.Count + 10 && list.Count(c => c == beatrix[0].CardId) >= 10);
                else Info($"{d.Name}: sideboard {picks} (not added to the deck)");
            }

            Section("Hotkeys");
            Check("Ctrl+Shift+H", Hotkeys.Parse("Ctrl+Shift+H", out var m1, out var v1) && m1 == 6 && v1 == 'H');
            Check("Alt+F2", Hotkeys.Parse("Alt+F2", out var m2, out var v2) && m2 == 1 && v2 == 0x71);
            Check("rejects Ctrl+Banana", !Hotkeys.Parse("Ctrl+Banana", out _, out _));

            Section("Lethal solver");
            LethalSolver.Attacker A(string n, int a, bool face = true, bool wf = false) => new LethalSolver.Attacker { Name = n, Attack = a, CanHitFace = face, Windfury = wf };
            LethalSolver.Taunt T(int h, bool shield = false) => new LethalSolver.Taunt { Name = "Taunt", Health = h, DivineShield = shield };
            var none = new List<LethalSolver.Spell>();
            Check("3+4 into empty board = 7", LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 3), A("b", 4) }, new List<LethalSolver.Taunt>(), none, 0, 7).Lethal);
            Check("2 kills a 2-health Taunt, 5 goes face for 5", LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 2), A("b", 5) }, new List<LethalSolver.Taunt> { T(2) }, none, 0, 5).Lethal);
            Check("Divine Shield Taunt needs two hits (1+1, then 5 face)", LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 1), A("b", 1), A("c", 5) }, new List<LethalSolver.Taunt> { T(1, true) }, none, 0, 5).Lethal);
            Check("...and with only one small hit it's not lethal", !LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 1), A("c", 5) }, new List<LethalSolver.Taunt> { T(1, true) }, none, 0, 5).Lethal);
            Check("a 3-damage spell clears the Taunt, 6 face",
                LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 6) }, new List<LethalSolver.Taunt> { T(3) },
                    new List<LethalSolver.Spell> { new LethalSolver.Spell { Name = "bolt", Cost = 2, Damage = 3, CanHitFace = true } }, 2, 6).Lethal);
            Check("...but not without the mana for it",
                !LethalSolver.Solve(new List<LethalSolver.Attacker> { A("a", 6) }, new List<LethalSolver.Taunt> { T(3) },
                    new List<LethalSolver.Spell> { new LethalSolver.Spell { Name = "bolt", Cost = 2, Damage = 3, CanHitFace = true } }, 1, 6).Lethal);
            Check("a Rush minion clears the Taunt, the other goes face", LethalSolver.Solve(new List<LethalSolver.Attacker> { A("rush", 4, face: false), A("x", 3) }, new List<LethalSolver.Taunt> { T(4) }, none, 0, 3).Lethal);
            Check("Windfury: one hit kills the Taunt, the second goes face", LethalSolver.Solve(new List<LethalSolver.Attacker> { A("wf", 3, wf: true) }, new List<LethalSolver.Taunt> { T(3) }, none, 0, 3).Lethal);
            var big = Enumerable.Range(0, 7).Select(i => A("m" + i, 2 + i % 3, wf: i == 0)).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LethalSolver.Solve(big, new List<LethalSolver.Taunt> { T(5), T(4, true), T(7) },
                Enumerable.Range(0, 5).Select(i => new LethalSolver.Spell { Name = "s" + i, Cost = 1, Damage = 2, CanHitFace = true }).ToList(), 10, 40);
            Check($"worst case (7 attackers, 3 Taunts, 5 spells) solves in {sw.ElapsedMilliseconds} ms", sw.ElapsedMilliseconds < 400);

            Section("Ladder estimate");
            var g55 = LadderEstimate.GamesToReach(31, 41, 0.55);
            var g60 = LadderEstimate.GamesToReach(31, 41, 0.60);
            Check($"Platinum 10 -> Diamond 10: {g55} games at 55%, {g60} at 60% (faster at higher win rate)", g55 > g60 && g60 > 0);
            Check("no climb at 50%", LadderEstimate.GamesToReach(1, 11, 0.50) == null);

            Section("Meta stats");
            Check($"archetypes loaded ({meta?.Archetypes.Count ?? 0})", meta != null && meta.Archetypes.Count > 10);
            if (meta != null && meta.Archetypes.Count > 0)
            {
                Check("shares add up to ~100%", Math.Abs(meta.Archetypes.Sum(a => a.Share) - 1.0) < 0.01);
                var withCards = meta.Archetypes.Count(a => a.Cards.Count > 0);
                Check($"card stats present ({withCards} archetypes)", withCards > meta.Archetypes.Count / 2);
                foreach (var d in saved.Take(10))
                {
                    var list = DeckCode.Decode(d.DeckCode, cards);
                    if (list == null || list.Count < 20) continue;
                    var arch = meta.MatchDeck(DeckCode.HeroClass(d.DeckCode, cards), list);
                    Info($"{d.Name} plays as {arch?.Name ?? "(no match)"}");
                }
            }

            Section("Game logs");
            ReplayAllLogs(cards, installDir, meta);

            _out("");
            _out($"RESULT: {_pass} passed, {_fail} failed");
            return _fail;
        }

        // ------------------------------------------------------------------ log replays

        private static void ReplayAllLogs(CardDb cards, string installDir, MetaStats meta)
        {
            var logsDir = installDir == null ? null : Path.Combine(installDir, "Logs");
            if (logsDir == null || !Directory.Exists(logsDir)) { Info("No Logs folder."); return; }
            var logs = Directory.GetDirectories(logsDir, "Hearthstone_*").Select(d => Path.Combine(d, "Power.log")).Where(File.Exists)
                .Where(f => new FileInfo(f).Length > 20000).OrderBy(File.GetLastWriteTimeUtc).ToList();
            Info($"{logs.Count} Power.log files with games");
            foreach (var log in logs) ReplayOne(cards, log, meta);
        }

        private sealed class GameCheck
        {
            public int Index;
            public int Turns;
            public int AdviceErrors;
            public string FirstError;
            public int Snapshots;
            public string Result;
            public int MaxOppMana;
            public int OppManaCap = 10;
            public string DeckDiff;
            public bool OppManaMonotonic = true;
            public int DeckListCount;
            public int StartedInDeck;
            public int UnresolvedNames; public string UnresolvedSample;
            public int FriendlyId;
            public string OppClass;
            public string GameType;
            public int Secrets;
            public int Choices;
            public int Review;
            public int Stolen;
        }

        private static void ReplayOne(CardDb cards, string path, MetaStats meta)
        {
            _out($"  {Path.GetFileName(Path.GetDirectoryName(path))}:");
            var queued = DecksLog.LastQueuedDeck(path);
            var deck = queued == null ? null : DeckCode.Decode(queued.DeckCode, cards);

            GameTracker tracker = null;
            Advisor advisor = null;
            GameRecorder recorder = null;
            var games = new List<GameCheck>();
            GameCheck cur = null;
            tracker = new GameTracker(cards, ev =>
            {
                recorder?.OnEvent(ev);
                if (ev.Kind == GameEventKind.GameStart)
                {
                    cur = new GameCheck { Index = games.Count + 1 };
                    games.Add(cur);
                    if (deck != null) { tracker.FriendlyDeckList = deck.ToList(); tracker.FriendlyDeckName = queued.Name; }
                }
                if (cur == null) return;
                if (ev.Kind == GameEventKind.Turn || ev.Kind == GameEventKind.GameEnd)
                {
                    try { advisor.Compute(); }
                    catch (Exception ex) { cur.AdviceErrors++; if (cur.FirstError == null) cur.FirstError = ex.GetType().Name + ": " + ex.Message + " @ " + FirstFrame(ex); }
                }
                if (ev.Kind == GameEventKind.GameEnd)
                {
                    cur.Turns = tracker.Turn;
                    cur.Result = tracker.Result;
                    cur.FriendlyId = tracker.FriendlyPlayerId;
                    cur.UnresolvedNames = tracker.UnresolvedNameLookups; cur.UnresolvedSample = string.Join(", ", tracker.UnresolvedNames);
                    cur.OppClass = tracker.HeroClass(tracker.OpponentPlayerId);
                    cur.GameType = tracker.GameType;
                    cur.DeckListCount = tracker.FriendlyDeckList?.Count ?? 0;
                    cur.StartedInDeck = tracker.Entities.Values.Count(e => e.StartedInDeck && e.StartController == tracker.FriendlyPlayerId);
                    if (tracker.FriendlyDeckList != null)
                    {
                        var seen = tracker.Entities.Values.Where(e => e.StartedInDeck && e.StartController == tracker.FriendlyPlayerId)
                            .Select(e => cards.ById(e.KnownCardId)?.Name).Where(n => n != null).ToList();
                        var missing = tracker.FriendlyDeckList.Select(id => cards.ById(id)?.Name ?? id).ToList();
                        var extra = new List<string>();
                        foreach (var id in seen) if (!missing.Remove(id)) extra.Add(id);
                        string Names(IEnumerable<string> ns) => string.Join(", ", ns.GroupBy(x => x).Select(gr => gr.Key + (gr.Count() > 1 ? " x" + gr.Count() : "")));
                        if (extra.Count > 0) cur.DeckDiff = $"seen but not in your list: {Names(extra)}";
                    }
                    var mana = tracker.OpponentTurns.Select(t => t.Crystals).ToList();
                    cur.MaxOppMana = mana.DefaultIfEmpty(0).Max();
                    cur.OppManaCap = tracker.MaxMana(tracker.OpponentPlayerId);
                    for (var i = 1; i < mana.Count; i++) if (mana[i] + 2 < mana[i - 1]) cur.OppManaMonotonic = false;
                    cur.Stolen = tracker.Entities.Values.Count(e => e.StartedInDeck && e.StartController == tracker.FriendlyPlayerId && e.Controller != tracker.FriendlyPlayerId);
                    // The recorder has just finished this game's record (it hears GameEnd first).
                    cur.Snapshots = recorder?.Current?.Snapshots.Count ?? 0;
                    cur.Review = recorder?.Current?.Review.Count ?? 0;
                    cur.Choices = tracker.Choices.Count;
                    cur.Secrets = tracker.RevealedSecrets.Count;
                }
            });
            // Your BattleTag: the player who queued (Decks.log is yours); the tracker also finds you from your hand.
            advisor = new Advisor(tracker, cards, meta);
            recorder = new GameRecorder(tracker, advisor, cards);
            tracker.Changed += recorder.OnChanged;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        lock (tracker.Sync) tracker.Feed(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Check($"    log replays without crashing ({ex.GetType().Name}: {ex.Message} @ {FirstFrame(ex)})", false);
                return;
            }

            if (games.Count == 0) { Info("    no games in this log"); return; }
            foreach (var g in games)
            {
                if (!GameModes.IsCardGame(g.GameType)) { Info($"    game {g.Index}: {GameModes.Name(g.GameType, null)} (only its mode and result are kept)"); continue; }
                var label = $"    game {g.Index} vs {MetaStats.ClassName(g.OppClass)}: {(g.Turns + 1) / 2} turns, {g.Result ?? "unfinished"}";
                if (g.Turns == 0) { Info(label + " (not finished in this log)"); continue; }
                _out(label);
                Check("      found which player is you", g.FriendlyId != 0);
                Check($"      every player name matched ({g.UnresolvedNames} unmatched{(g.UnresolvedNames > 0 ? ": " + g.UnresolvedSample : "")})", g.UnresolvedNames == 0);
                Check($"      advice computed every turn{(g.FirstError != null ? $" - {g.AdviceErrors} errors, first: {g.FirstError}" : "")}", g.AdviceErrors == 0);
                if (g.DeckListCount > 0)
                    Check($"      your starting deck: {g.StartedInDeck} cards tracked vs {g.DeckListCount} in your list{(g.DeckDiff != null ? " (" + g.DeckDiff + ")" : "")}", Math.Abs(g.StartedInDeck - g.DeckListCount) <= 1);
                Check($"      opponent mana crystals tracked (max {g.MaxOppMana}, never drops)", g.MaxOppMana >= 1 && g.MaxOppMana <= g.OppManaCap && g.OppManaMonotonic);
                Check($"      game recorded ({g.Snapshots} turn snapshots, {g.Review} review notes)", g.Snapshots >= Math.Max(1, g.Turns - 2));
                Info($"      {g.Choices} choices, {g.Secrets} secrets revealed{(g.Stolen > 0 ? $", {g.Stolen} of your cards stolen" : "")}");
            }
        }

        private static string FirstFrame(Exception ex)
        {
            var st = ex.StackTrace ?? "";
            var line = st.Split('\n').FirstOrDefault(l => l.Contains("HearthStoner")) ?? st.Split('\n').FirstOrDefault() ?? "";
            return line.Trim();
        }

        // ------------------------------------------------------------------ helpers

        private static void Profile(CardDb cards, string id, string name, Func<CardProfile, bool> ok)
        {
            var c = cards.ById(id);
            if (c == null) { Check($"{name}: card {id} not in the card database", false); return; }
            Check($"{name}: {c.Profile.Summary()}", ok(c.Profile));
        }

        private static void Section(string name) { _out(""); _out("== " + name); }
        private static void Info(string s) => _out("  .    " + s);

        private static void Check(string what, bool ok)
        {
            if (ok) _pass++; else _fail++;
            _out($"  {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
