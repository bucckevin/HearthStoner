# HearthStoner

A Windows console app that reads your Hearthstone data:

- **Collection, decks, account and match info** straight from game memory, using [UnitySpy](https://github.com/Zero-to-Heroes/unity-spy-.net4.5) (MIT). That's the open-source HearthMirror replacement maintained by the Firestone tracker, vendored in `vendor/`.
- **Live gameplay** (draws, plays, deaths, turns, results, cards left in your deck) from Hearthstone's `Power.log`, combined with memory reads.

## Requirements

- Windows 10/11, 64-bit
- The [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. It includes the .NET 10 Desktop Runtime that the app runs on
- Hearthstone running and sitting at the main menu when reading memory

## Install

Double-click `install.bat`. It builds one `HearthStoner.exe` into `%LOCALAPPDATA%\Programs\HearthStoner`, adds HearthStoner to the Start menu (with its icon) and starts it. Run it again to update to the latest source. No admin rights needed; it uses the .NET 10 runtime that comes with the SDK. To remove it, use Windows' Settings > Apps > HearthStoner > Uninstall (or `HearthStoner uninstall`). That removes the program, the shortcut and start-with-Windows, and keeps your data in `%LOCALAPPDATA%\HearthStoner`.

## Build

Double-click `build.bat` (output goes to `build.log`), or:

```
dotnet build HearthStoner.slnx -c Release
dotnet test HearthStoner.slnx            # the tests
```

The exe lands in `bin\Release\net10.0-windows\HearthStoner.exe`.

The solution has four projects:

| Project | What's in it |
|---|---|
| `HearthStoner.csproj` (Windows, .NET 10) | The commands (`Program.cs`), the overlay (`Overlay/`) and the tray app (`App/AppHost.cs`, `App/MainWindow.cs`) |
| `Core/HearthStoner.Core.csproj` (.NET 10, any OS) | All the logic, in `Core/`: `Game/`, `Advice/`, `Meta/`, `Knowledge/`, `Stats/`, deck codes, card data, settings, memory reads. Every .cs file in `Core/` is part of it |
| `vendor/UnitySpy.csproj` | UnitySpy, the memory reader (MIT) |
| `tests/HsTest/HsTest.csproj` | xUnit tests: unit tests, plus every saved game replayed through the full self-test |

## Use

```
HearthStoner setup                          # enables Power logging; restart Hearthstone once afterwards
HearthStoner collection                     # every card you own, grouped by class, with finishes
HearthStoner collection --csv cards.csv     # also export (or --json cards.json)
HearthStoner decks                          # your saved decks, each with a deck code you can paste into the game
HearthStoner status                         # account, current screen, current match + ranks
HearthStoner app                            # the full app: tray icon + overlay + window (deck picker, tuning, crafting, stats, history, meta)
HearthStoner overlay                        # in-game overlay with all the advice below; Ctrl+C to stop
HearthStoner watch --tips                   # same advice in the console, printed every turn
HearthStoner meta                           # today's Standard archetypes: popularity and win rate
HearthStoner stats                          # your win rates vs the meta, mulligan habits, tilt check, games to next rank
HearthStoner review 3                       # post-game review of your last 3 games, turn by turn
HearthStoner puzzles                        # re-solve turns where you had lethal
HearthStoner quiz                           # mulligan quiz from your own opening hands
HearthStoner quests                         # your quests and progress
HearthStoner replay <Power.log> --deck CODE # run a saved game log through the advisor
HearthStoner find Collection                # search Hearthstone's class names
HearthStoner explore CollectionManager.s_instance          # see an object's fields and live values
```

Add `--verbose` to any command to see UnitySpy's internal log.

## In-game advice (overlay)

`HearthStoner overlay` lays a click-through panel over each side of the Hearthstone window (run the game windowed or borderless fullscreen):

- **Health & damage (top of each side):** effective health (HP + armor) for both heroes, what each board can hit for (now if it's that side's turn, otherwise next turn; Charge, Windfury, weapons, frozen minions and Taunts counted), your burn in hand added for a lethal check, their likely burn or Charge cards that would make it lethal, total attack and health on each board, and a fatigue warning.
- **Opponent (left):** their most likely archetypes, given the cards they've shown and how popular each archetype is. Also the cards they probably still hold, with board clears flagged, and each card in their hand with where it came from (kept in mulligan, drawn, created by X, returned to hand) and how long they've held it.
- **Secrets (left):** what a hidden secret can still be. Candidates are ruled out as you do things that would have triggered them, like attacking, casting a spell or playing a minion. Each candidate comes with advice on playing around it.
- **Mulligan (right):** keep or toss each card, using real win-rate-when-kept stats for your deck's archetype in this exact matchup.
- **Board (right):** lethal checks, a warning when their board can kill you, their biggest threats with notes ("Divine Shield: ping it first"), which cards in your hand or which trades answer each one, and board clears to play around.
- **Your deck (right):** a deck-tracker list in the style of HSReplay's: a bar per card with its mana cost, art and a count box (2+ copies, or a star for a legendary), dimmed when every copy is drawn, plus each card's chance to be your next draw (turn off with `DeckShowOdds`). Below it, the chance your next draw is removal, AoE, healing or card draw.

It only uses information you're allowed to see: the game log your client writes, your own client's public state and aggregate meta stats. It never reads hidden opponent information and never sends input to the game.

Data sources:
- **Card text and stats:** [HearthstoneJSON](https://hearthstonejson.com). HearthStoner reads each card's text to profile it as removal, AoE, burn, healing, draw and so on (`Core/Knowledge/CardProfile.cs`).
- **Meta stats:** [Firestone](https://www.firestoneapp.com)'s public Constructed data (archetypes, per-card mulligan and draw win rates, per-matchup splits), refreshed every 3 hours. Change the rank bracket with `--rank legend` and the period with `--period last-patch`.
- **Secrets:** the 11 Standard secrets, with their triggers and play-around advice, are hand-written in `Core/Knowledge/Secrets.cs`.

Update `CardDb.StandardSets` and `Core/Knowledge/Secrets.cs` when Standard rotates (each March).

`setup`, `watch` and `overlay` also set `FileSizeLimit.Int=-1` in `client.config` in your Hearthstone folder. Without it, Hearthstone stops writing Power.log after about 10 MB, which is a few games, and tracking silently stops.

### Overlay extras

- **This turn:** the ways to spend all your mana with the cards the game says you can play right now, plus your hero power.
- **Choose:** when you Discover, the win rate of each option when it was picked by players of your archetype (Firestone data).
- **Opponent's hand:** a mark over each card in their hand: the turn they got it (T3), `Mull` for cards from their opening hand (kept or redrawn), `Coin`, `New` for cards created by another card (with the card that made it), `Back` for a card returned to their hand (with its name), and `Yours` for a card stolen from you. Turn off with `ShowOpponentHandMarks`; Ctrl+Shift+O hides it with the opponent panels. Positions follow the hand's fan for 1 to 10 cards at any window size.
- **Opponent played:** every card your opponent has played, with art, the turn they last played it, and whether it was created by another card.
- **Hover mode:** press Ctrl+Shift+M and point at a card bar to see the full card picture. Press it again to give the mouse back to the game.
- **Reads (opponent panel):** inferences from public information. For example: they floated 3 mana with 5 cards, so they're likely holding reactive or expensive cards. They played nothing with 2 mana, so they probably have a slow hand. Cheap cards they had the mana for but didn't play are probably not in their hand. Cards kept since the mulligan and never played are often combo pieces.
- **Lethal through Taunts:** when Taunts are in the way, HearthStoner works out whether your attacks and damage spells can clear them and still reach face, then shows exactly which minion or spell should go where. Hero powers that hit face (Hunter, Mage and others) count toward your lethal and toward what your opponent threatens.
- **Outs:** when their board threatens lethal, the cards left in your deck that would save you, with the chance of drawing one next. When yours threatens lethal, their likely cards that could stop it.
- **Matchup cheat sheet** (mulligan and the first turns): your archetype's win rate in this matchup, your best and weakest cards in it, and the likely enemy archetype's key cards, with board clears flagged.
- **Counters:** spells cast, minions died, corpses, hero power uses, cards played this turn and cards drawn. Only the counters that cards in your deck care about are shown.
- **Animations:** the card you just drew flashes gold in the deck list, and lethal and danger warnings pulse.
- **Hotkeys:** Ctrl+Shift+H shows or hides the overlay, Ctrl+Shift+D the deck list, Ctrl+Shift+O the opponent panels, and Ctrl+Shift+A the advice.

### Settings

`%LocalAppData%\HearthStoner\settings.json` is created the first time HearthStoner runs. In it you can set panel width, opacity, text size, card art on or off, swapping sides, each panel on or off, the hotkeys, and the meta bracket. The meta bracket defaults to `auto`, which uses the rank from your last game. Restart HearthStoner after editing.

## The app (`HearthStoner app`, or the Start menu shortcut)

This runs without a console window. It puts a tray icon near the clock with these menu items: Open, Overlay on/off, Start with Windows, and Quit. The overlay appears whenever Hearthstone is in front. The window has a navigation rail with eight pages:

- **Play:** a button to launch Hearthstone through the Battle.net app (`Battle.net.exe --exec="launch WTCG"`), which turns into "Switch to Hearthstone" with a green dot while the game runs. Battle.net is found from the registry; Settings shows which program it uses and lets you choose Battle.net.exe or Hearthstone.exe yourself. Then your decks, ranked by expected win rate against today's field at your rank. The best one is a banner of its signature card's art, with its best and worst common matchups and a button to copy its deck code. On the right: the field's class mix with that deck's win rate against each class, and what to craft next. Under your decks, **meta changes**: HearthStoner keeps a daily snapshot of the meta (`meta-history.json`) and, compared with a few days ago, points out when a deck's expected win rate moved by 1.5 points or more, or an archetype's share of games changed noticeably.
- **Decks:** each deck's card list, the swaps the data favours (for example Inspiring Maul, 41% when drawn, for Ursol, 55%), and how your list differs from the best-performing list of its archetype, with the dust to build it. Paste any deck code to analyze it the same way. **Versions:** every list you've played a deck with (from your game records, matched by name or by 24+ shared cards), its record, and the cards that changed from the version before.
- **History:** every recorded game with its result, key numbers, a **win-chance graph** (an estimate at every moment from health, boards, hands, unspent mana and lethal threats, starting from the matchup's win rate in the meta data; the biggest drops are marked and click through to the replay), the review and your mulligan.
- **Replays:** step through any saved game: both boards, hands, mana and the advice HearthStoner showed, with the win chance under the board and review notes on the timeline. **Game art (optional):** run `HearthStoner gameart` once to pull the real board, minion and hero frames, end-turn button, armor, divine shield and freeze art out of your own Hearthstone install into `%LocalAppData%\HearthStoner\gameart` (and to render every game board for replays). It only reads the game's files. The replay board uses that art when it's there, and draws its own otherwise. The art is Blizzard's, so keep it on your PC and don't share it; run it again after a patch. For the whole table (frame, corners, hero slots), save a 1024x512 picture of a board as `table.png` in that folder, such as the board images on hearthstone.wiki.gg; the replay lines its heroes, minions, end-turn button and play history up with that layout.
- **Practice:** a mulligan quiz from your own opening hands and lethal puzzles from your own games.
- **Stats:** your record, win rate going first and on the coin, recent form, legend rank over time, how early the overlay reads the opponent's deck, **mana use** (share of mana spent on turns 1-10, how often you use all of it on turns 1-6, average spent per turn, the turn you stall on, by deck), results by opponent class and by deck, mulligan habits, tilt check and ladder.
- **Meta:** every archetype by class, with play rate and win rate.
- **Settings:** overlay layout and panels, the meta bracket and period, start with Windows, reading your collection, and the hotkeys. Saved as you change them.

The look: a dark indigo theme with Barlow and Barlow Condensed (SIL Open Font License, bundled in `App/Fonts`). Card art comes from HearthstoneJSON and is cached in `%LocalAppData%\HearthStoner\art`. `HearthStoner screenshot [folder]` renders every page to PNGs.

Console output goes to `%LocalAppData%\HearthStoner\hearthstoner.log`. Crafting needs your collection: the app reads it from Hearthstone automatically once the game is at the main menu, or from Settings.

## Game history and review

Every game played with `overlay` or `watch` running is saved to `%LocalAppData%\HearthStoner\games\` as a JSON file. Each file includes your deck, the opponent's class and likely archetype, the result, rank, mulligan, Discover picks, and a snapshot of every turn. After each game, a review lists likely mistakes:

- possible missed lethal
- ending a turn with unspent mana while a card was playable
- overextending into a board clear that HearthStoner had flagged as likely
- mulligan choices against the data
- Discover picks that win less than another option
- secrets you ran into

With the game's log saved, the History page adds notes worked out from the replay, each with a link to that moment:

- cards held for four or more turns while affordable, with mana left over on at least one of them
- the hero power left unused with mana to spare (one note per game)
- trades where your minion died and theirs survived the turn (divine shields not counted)
- attacking face when their board could kill you, and dying the next turn
- where the win chance fell the most

The review shows in the console and on the overlay until the next game starts. `HearthStoner stats` combines all your games: win rate per deck and matchup next to the meta's, mulligan habits, tilt patterns (after losses, long sessions, time of day), an estimate of games to the next league, and which mistakes keep coming up.

## Practice and upkeep

- `HearthStoner selftest`: checks HearthStoner against your own saved game logs and known card answers, and writes the results to `selftest.log`. Run it after updating HearthStoner or after a Hearthstone patch.
- `dotnet test HearthStoner.slnx`: the unit tests plus the full self-test over your saved games. On Windows it uses your real HearthStoner data and Hearthstone logs. `tests/run.sh` does the same on Linux or macOS (no Windows or Hearthstone needed). It builds everything, including the Windows app, and installs the .NET 10 SDK into `/opt/dotnet` if it's missing. Test data goes in `tests/data`: copies of `%LocalAppData%\HearthStoner` (cards.json and meta/) under `tests/data/appdata/HearthStoner`, and copies of Hearthstone's `Logs\Hearthstone_*` folders under `tests/data/install/Logs`. Every change is built and tested this way before it ships.
- **Two groups of tests.** `dotnet test HearthStoner.slnx --filter Category!=MyGames` runs the fixed tests (same inputs every time, about a second). `--filter Category=MyGames` runs the ones that replay your own logs and saved games, which grow as you play. So a failure tells you whether the code broke or a new game exposed something.
- **Saved games are tests.** Every game saved with its log is replayed by `dotnet test`: recording it again must give the same result, length, classes and turn-by-turn health and boards, the replay must obey the game's limits (hand, board, mana), and the win chance and review must hold up. A Hearthstone patch that changes the log fails here after your first game on it. On Linux, copies of the games go in `tests/data/appdata/HearthStoner/games`.
- `HearthStoner preview [out.png] [--log Power.log] [--turn N]`: draws the overlay as it looked at turn N (default 12) of your latest game into a PNG. `build.bat` makes `screenshots\preview.png` after every build.
- `HearthStoner puzzles`: every time HearthStoner sees you have lethal, it saves the board, hand and mana. Re-solve them later; the ones you missed are marked.
- `HearthStoner quiz`: a mulligan quiz built from your own opening hands, scored against the win-rate data.
- `HearthStoner quests`: your quests and progress from memory. For now it also shows the raw quest record fields.
- **Update checks:** once a day HearthStoner checks whether the Firestone team has published a newer UnitySpy (memory reading). It also tells you when Hearthstone's client build changes, which is when memory reading usually breaks.

## How memory reading works

Hearthstone is a Unity game running on the Mono runtime. UnitySpy opens the process **read-only**, finds Mono's root domain through `mono_get_root_domain` in `mono-2.0-bdwgc.dll`, and walks to the `Assembly-CSharp` image. From there it reads classes, static fields and object fields using Mono's own metadata. Nothing is injected or written.

The data lives at paths like:

| Data | Path in memory |
|---|---|
| Collection | `CollectionManager.s_instance.m_collectibleCards` (each card: `m_EntityDef.m_cardIdInternal`, `<OwnedCount>k__BackingField`, `m_PremiumType`) |
| Decks | `CollectionManager.s_instance.m_decks` (`keySlots` = deck ids, `valueSlots` = decks with `m_name`, `m_slots`, `<HeroCardID>k__BackingField`) |

## When a Hearthstone patch breaks something

1. Check whether the vendored library has been updated upstream: compare `vendor/UNITYSPY_COMMIT.txt` with the latest commit at https://github.com/Zero-to-Heroes/unity-spy-.net4.5 . If it has, copy the new `UnitySpy` and `UnitySpy.HearthstoneLib` folders into `vendor/`.
2. Or fix it yourself: `HearthStoner explore CollectionManager.s_instance` shows the live field names. Update the path in `Core/HsMemory.cs` or the vendored reader.

## Files

| File | What it does |
|---|---|
| `Core/HsMemory.cs` | Thin layer over UnitySpy: attach, collection, all decks, match/account info, `find`/`explore` |
| `Core/PowerLog.cs` | Follows the newest `Logs\Hearthstone_*\Power.log` |
| `Core/Game/GameTracker.cs` | Rebuilds the game from Power.log: entities, zones, mana, hands, origins, mulligan, secrets |
| `Core/Advice/Advisor.cs` | Deck prediction, secret helper, mulligan, threats and answers, lethal checks, draw odds |
| `Core/Meta/MetaStats.cs` | Downloads and caches Firestone's meta stats |
| `Knowledge/` | Card profiles from card text, and the Standard secrets table |
| `Overlay/OverlayWindow.cs` | The click-through WPF overlay |
| `Core/DeckCode.cs` | Builds importable deck codes |
| `Core/CardDb.cs` | Card names from HearthstoneJSON, cached in `%LocalAppData%\HearthStoner` |
| `Core/HearthstoneLocator.cs` | Finds the install via the registry and writes `log.config` |
| `Program.cs` | The commands |
| `vendor/` | UnitySpy sources (MIT, see `vendor/LICENSE-UnitySpy.txt`) |

## A note on Blizzard's rules

Reading log files is what Blizzard expects trackers to do. Reading memory read-only is how HDT and Firestone work, and Blizzard has tolerated it for years, but it isn't officially approved. HearthStoner never writes to the game or automates play.
