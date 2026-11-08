# Changelog

Every installed version, newest first. The number is in `VERSION`: patch (1.0.x) for fixes, minor (1.x.0) for new
features, major (x.0.0) for big changes, such as a new data format. Each version is tagged in git as `v<number>`.

## 1.5.0 - 2026-11-08

The Ladder, History and Stats pages tell the game modes apart, and two crashes-in-waiting are fixed.

- **Ladder:** Standard and Wild tabs instead of both stacked (it opens on the format you play most this season). A
  rank card shows your stars and how far it is to the next floor, with the star bonus and win streak beside it.
  The climb chart is shorter and says how many games aren't on it (games added from logs have no rank). Goals drop
  the "a day" column (it's on hover). Decks are per format, and Wild no longer gets Standard meta estimates. A format
  with fewer than 3 games this season gets a short version. "Best time to play" moved to Stats.
- **History:** a filter chip for each mode you've played (Ranked, Casual, Brawl, Arena, Solo), with counts.
  Battlegrounds games are listed with your place and hero; a top-half finish is green (top 4, or top 2 of the 4
  teams in Duos).
- **Stats:** ranked and casual games by default, the ones the advice is about, or every card mode with one click.
  Before, 4 wins against the AI and the Arena and Brawl games counted, and adventure bosses showed as "Neutral".
- **Fixed:** the app crashed as a game ended when two minions had "infinite" attack (Hand of Infinity), and that
  game wasn't saved (it's recovered from the log). A practice or solo game no longer gets the last deck you queued,
  or its archetype: Decks.log's deck only counts for the first game after it, live and when importing (before,
  only a deck of another class was caught). "1 times in 1 games" reads "once in 1 game".
- **Removed modes:** Duels, Friendly challenges and the Twist format are gone from Hearthstone, so they're no longer
  named or offered as filters.

## 1.4.1 - 2026-11-08

Fixes from a night of playing every mode: Arena, Battlegrounds, Ranked Wild and Tavern Brawl.

- **Arena:** games are saved with your Arena deck, named "Arena deck", not the last ranked deck you queued
  (Decks.log writes "Starting Arena Game With Deck:" there). No ranked-deck archetypes for drafted decks.
- **Battlegrounds:** your place (1st to 8th) and hero are saved, and History's "Other modes" shows your average
  place and top-4 finishes instead of wins. No more "Bartender Bob" as the opponent.
- **Opponent class:** found even when they turn into a classless hero card (Deathwing, Worldbreaker) or use a
  hero skin the card list doesn't know. Solo adventure bosses show by name instead of "Neutral".
- **Ranks** are only kept for ranked and casual games; Arena and Battlegrounds games no longer carry your Wild rank.
- **Your deck** isn't loaded from Decks.log when it's a different class from your hero (a deck from an earlier
  game), and no deck is loaded in Battlegrounds or Mercenaries.
- **Old games** saved with these mistakes are repaired once from their logs.
- **History:** the win-chance graph fits the window (a small window cut off the end of the game), and the numbers
  above it wrap to a second row instead of running off the edge.

## 1.4.0 - 2026-11-07

A UI review: the window fits any screen size, and every page got clearer.

- **Any screen size:** pages stack into one column in a small window, use two in a normal one, and three side by
  side when maximized on a 1920 px or ultrawide screen (centred past 2400 px). Crossing a size keeps your place.
- **Display settings:** text size from 90% to 150%, and the window opens where you left it (maximized if it was;
  back to the middle if that screen is gone). Ctrl+1 to Ctrl+9 open the pages in menu order.
- **Play:** on a wide screen the field shows all eight classes and quests and crafting sit beside it.
- **Decks:** your own record under each deck's win rate, "No data" for decks the meta doesn't know, a shorter
  results-by-class list.
- **History:** filter by result (wins, losses) as well as mode, a message and a reset button when nothing
  matches, and "Clean" instead of an empty review. On an ultrawide screen the review sits beside the graph.
- **Ladder:** lost games are hollow rings on the climb chart and the extra-stars note is clearer.
- **Stats:** W and L letters on the last 30 games; decks that weren't saved read "Paladin, deck not saved".
  Your rank comes from your Standard ranked games, not a newer Wild brawl.
- **Practice:** a progress panel (answers per day for two weeks, accuracy per drill, questions learned). "What
  would you play?" keeps your plan, mana crystals and Lock in beside the board. Lethal puzzles use the app's
  colours and real card art.
- **Replays:** the advice is grouped into cards, a bit larger, and "their deck" no longer sits under your name.
- **Meta:** names sit next to their bars.
- **Settings:** two columns on a wide screen.
- Rounded boxes clip their art to the rounded corners (the Best pick banner's right end was square).
- Greyed-out buttons are flat grey and say why they're greyed out.
- Your name shows in the menu even when HearthStoner starts before Hearthstone.
- The settings file is saved safely when two saves happen at once.
- Self-test: each game uses the deck queued before it, not the session's last deck.

## 1.3.1 - 2026-11-07

Dependency updates (from Dependabot).

- Newtonsoft.Json 13.0.4 (the app's JSON reading and writing).
- Tests: xunit 2.9.3, xunit.runner.visualstudio 4.0.1, Microsoft.NET.Test.Sdk 18.11.0. UnitySpy build-time
  annotations: JetBrains.Annotations 2026.2.0.
- GitHub workflows: actions/checkout v7, actions/setup-dotnet v6, actions/upload-artifact v7.

## 1.3.0 - 2026-11-07

Downloads and updates from GitHub.

- Releases: pushing a version tag makes GitHub build, test and publish `HearthStoner.exe` (with its SHA-256) as a
  release. Every push is also built and tested on GitHub.
- HearthStoner updates itself: once a day (with "Check for updates" on) it looks for a newer release. Settings shows
  your version with Check now, What's new and Update buttons; the tray menu gets "Update to x.y.z" and one
  notification. The download is checked against its SHA-256, then installed when no game is in progress.
- `HearthStoner update [--check]` does the same from the command line.
- `install.bat` and GitHub publish with the same settings, kept in `HearthStoner.csproj`.

## 1.2.0 - 2026-11-07

Practice drills (built on the `practice-drills` branch).

- Practice home: today's review (questions you missed come back the next day, then after 3 and 7 days; three right
  in a row retire one; saved in practice.json), your streak, and a tile per drill with what's due.
- New drills: What would you play? (a turn from your replay; scored on mana use, shows what you played and how your
  win chance moved), Guess their hand (cards revealed later), Secret guessing (revealed secrets), Discover drill.
- Mulligan quiz becomes Mulligan by matchup: opponent-class chips and a keep/toss table for the matchup.
- Lethal puzzles: after the solution you mark whether you found it, so missed ones come back in the review.
- `screenshot` also draws every drill, asked and answered.

## 1.1.1 - 2026-11-07

- Quests, collection, ranks and decks read from the game again when HearthStoner started before Hearthstone: an
  attachment made while the game was still loading only read empty and was never made again. Now one that reads
  nothing for 90 seconds is made again (logged as "[memory] reads stayed empty...").
- Card art: black bands some paintings have at their edges are trimmed (the dark strip on the Hunter deck's row).

## 1.1.0 - 2026-11-07

The UI review (built on the `ui-review` branch).

- Overlay: OPPONENT / YOU headers with HP on the right; no percentage on near-certain cards; their hand as one row of
  chips (turns held, long-held ones gold); board threats as rows with keyword tags; amber for warnings.
- Every page: an icon per page in the menu, brighter and larger small text, amber (not gold) for "look at this".
- Play: decks of the same archetype share one row; a dark fade behind each percentage; field table headings with
  3 classes behind "more"; Battlegrounds quests collapsed.
- Decks: swaps apart from weak cards with nothing better owned; your results by opponent class.
- Ladder: best-time bars show the win rate, too-few-games marked; unplayed decks quieter, a "meta" tag for estimates.
- History: filter chips, a header per day, the mulligan as card pictures.
- Replays: turn, whose turn and win chance above the advice panel, which now looks like the overlay.
- Practice: real Keep / Toss buttons; lethal puzzles drawn as a board.
- Stats: last 30 games read older to newest, hover and click a square; mulligan habits and tilt check as tables.
- Meta: search, view chips (by class, one list, only your decks), sort by games or win rate, your archetypes marked.

## 1.0.2 - 2026-11-07

- Settings: "Where the panels go" now reads Left, Right, then Opponent left, you right (Right is still the default).

## 1.0.1 - 2026-11-07

- The version shows just the number and build date, without the git commit.

## 1.0.0 - 2026-11-07

The first numbered version: everything HearthStoner does up to now, plus tonight's cleanup.

- Self-contained install: one exe with the .NET runtime inside, so it runs without .NET installed. The installer
  removes the WPF DLLs older builds unpacked into `%TEMP%\.net`.
- Battlegrounds quests are shown apart from your normal quests and left out of the quest planner.
- The version shows on the Settings page, in the tray menu and by `HearthStoner --version`.
- Under the hood: the code is in git, the logic lives in `Core/` (one namespace per folder), the live pipeline is
  `GameSession`, caught errors go to `hearthstoner.log`, and the tests are split into fixed tests and your own games.
