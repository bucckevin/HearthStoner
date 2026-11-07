# Changelog

Every installed version, newest first. The number is in `VERSION`: patch (1.0.x) for fixes, minor (1.x.0) for new
features, major (x.0.0) for big changes, such as a new data format. Each version is tagged in git as `v<number>`.

## 1.0.0 - 2026-11-07

The first numbered version: everything HearthStoner does up to now, plus tonight's cleanup.

- Self-contained install: one exe with the .NET runtime inside, so it runs without .NET installed. The installer
  removes the WPF DLLs older builds unpacked into `%TEMP%\.net`.
- Battlegrounds quests are shown apart from your normal quests and left out of the quest planner.
- The version shows on the Settings page, in the tray menu and by `HearthStoner --version`.
- Under the hood: the code is in git, the logic lives in `Core/` (one namespace per folder), the live pipeline is
  `GameSession`, caught errors go to `hearthstoner.log`, and the tests are split into fixed tests and your own games.
