# Services — non-UI logic

This directory contains the application's core behavior: files and shortcuts,
process launching, global mouse and keyboard hooks, folder watching, sorting,
icons, and link metadata. It does not render windows; presentation belongs in
`../UI`.

## Contents by area

- Storage and data: `TargetStore` (loading and saving `config.json`,
  deduplication, and configuration sanitization), `PresetService`, and
  `SortMigration`.
- Files and drops: `FileOps` (Explorer-style copy and move), `DropDispatch`
  (drop priority), `VirtualFileService` (browser and mail drags),
  `TextDropService`, `LinkTargetService`, and `FileMeta` (file properties used
  by rule conditions).
- Launching: `LaunchService` (open a target or launch it with files),
  `ShortcutResolver` (`.lnk` parsing), `StartupService`, and
  `TelegramDropService`.
- Input and presence: `MouseHook`, `KeyboardHook`, `HotkeyService`, `OrbGesture`,
  `GroupShortcutActivation`, `GroupShortcutSequence`, `ProximityState`,
  `FullscreenDetector`, and `CursorTargetLocator`.
- Sorting and layout: `SortService`, `WheelLayout`, `WatcherService` (automatic
  sorting for watched folders), `HintPolicy` (hint display limits),
  `IconService`, `LinkMetadataService`, and `ErrorLog`.
- History and integration: `DropHistoryService` (recent drops in the tray menu)
  and `ExplorerBridgeService` (the Explorer "Send to → Dropwheel" entry).

## Working with services

`../UI` calls these services. Many methods are deliberately pure and
`internal static` so `tests/Dropwheel.Tests` can exercise them without starting
a window. Search for callers with `ck` before editing a service.

## Guidelines

- Add a new service in its own focused file and prefer pure, testable functions.
- Do not open windows or manipulate WPF visuals here, except for existing
  window-oriented services such as capture hints. Presentation belongs in
  `../UI`.
- Do not swallow exceptions silently. Log through `ErrorLog` or propagate them.
