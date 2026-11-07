# Changelog

Every installed version, newest first. The number is in `VERSION`: patch (1.0.x) for fixes, minor (1.x.0) for new
features, major (x.0.0) for big changes, such as a new data format. Each version is tagged in git as `v<number>`.

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
