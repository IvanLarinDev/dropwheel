# Dropwheel
<img width="460" height="460" alt="PixPin_2026-07-05_15-25-48" src="https://github.com/user-attachments/assets/becab6e4-a227-4759-875a-3cfd84b63fcb" />


[![CI](https://github.com/IvanLarinDev/dropwheel/actions/workflows/ci.yml/badge.svg)](https://github.com/IvanLarinDev/dropwheel/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/IvanLarinDev/dropwheel)](https://github.com/IvanLarinDev/dropwheel/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Overlay launcher for Windows 10/11: a floating orb that expands into a radial
**wheel** of targets (folders and apps). Drop files onto targets to copy or
move them — the action is controlled by a global setting with per-target
overrides. The wheel opens by itself when a drag approaches the orb.

## Install

Grab the [latest release](https://github.com/IvanLarinDev/dropwheel/releases/latest):

- `Dropwheel-vX.Y.Z-win-x64.zip` — small; requires the .NET 10 Desktop Runtime
- `Dropwheel-vX.Y.Z-win-x64-self-contained.zip` — larger; no runtime needed

Unzip anywhere and run `Dropwheel.exe`. Config lives in `%AppData%\Dropwheel\config.json`.

## Build from source

    cd src/Dropwheel
    dotnet run

Requires the .NET 10 SDK (Windows). `run.cmd` at the repo root wraps the
common loops: `run.cmd [run|build|publish|stop]`. Run the tests with
`dotnet test` (xUnit, in `tests/Dropwheel.Tests`).

## Controls

| Action                | How                                                |
|-----------------------|----------------------------------------------------|
| Open the wheel        | hover the orb (250 ms), click it, or drag a file near it |
| Drop a file           | drag onto a target tile; badge shows ⧉ copy / ➜ move |
| Force copy / move     | hold Ctrl / Shift while dropping                   |
| Undo last drop        | click “Undo” in the toast (6 s)                    |
| Edit a target         | right-click its tile                               |
| Add a target          | drop a folder/exe onto the “+” tile or the orb     |
| Create a group        | right-click the orb → “New group…”                 |
| Enter a group         | click its tile, or hover it for 0.5 s while dragging |
| Move the orb          | Alt + left-drag (any monitor)                      |
| Wheel at cursor       | Ctrl+Alt+Space (configurable)                      |
| Settings              | tray icon or orb context menu                      |

The orb hides automatically in full-screen apps (games, presentations) and can
fade out when idle (see Settings).

## Sorter targets & routing rules

![Routing rules editor](docs/media/routing-rules.gif)

A **sorter** distributes dropped files into subfolders by rules (amber-bordered
tile, ⇅ badge). Right-click a target and press **Convert to routing rules**, or
start from a **Presets ▾** category (Images, Documents, Archives, …). Rules are
an ordered list edited in a master–detail panel — the first rule whose
conditions all match wins:

- **Extension** — `png jpg webp` (space- or comma-separated, dots optional)
- **Name contains** / **Name regex** — match against the file name
- **Size (MB)** and **Age (days)** — with `>`, `<`, `≥`, `≤`
- a rule with no conditions is a **catch-all**

Each rule sends its matches to a subfolder (relative to the target `Path`) or an
absolute folder. The **Test files** box previews which sample files land in the
selected rule. Presets live in `config.json` under `Presets` and are yours to edit.

![Type presets](docs/media/presets.gif)

The legacy extension map still loads and is migrated to rules on first edit:

    { "Name": "Sort", "Path": "D:\\Sorted",
      "SortRules": { "jpg png webp": "Images", "pdf docx": "Docs", "*": "Other" } }

Undo reverts the whole batch.

## Themes

![Themes](docs/media/themes.gif)

Four themes — Fluent, Dark, Light, Neon — chosen in Settings. Each carries a full
palette: the wheel, the target editor and settings windows, the orb context menu,
and the tray menu all follow the theme (accent colour, surfaces, text). Group and
sorter tile borders are tuned per theme.

## Project layout

    src/Dropwheel/
      Models/    TargetItem, AppConfig, SortRule (conditions), FilePreset
      Services/  TargetStore (JSON config), FileOps (SHFileOperation),
                 VirtualFileService, SortService, SortMigration, FileMeta,
                 PresetService, ShortcutResolver, MouseHook, HotkeyService,
                 LaunchService, IconService, StartupService, FullscreenDetector
      UI/        OverlayWindow (hub + rim + spokes wheel, partial classes),
                 TargetEditorWindow (+ .Rules master-detail), SettingsWindow,
                 Themes, Palette (per-theme widget colours), MenuTheme.xaml
    tests/       Dropwheel.Tests (xUnit: SortService, SortMigration, FileMeta)
    docs/media/  screenshots and gifs used by this README

## Known limitations

- Dragging from elevated (admin) processes does not work — Windows UIPI.
- Virtual files (Outlook attachments, browser drags) are always copied;
  files renamed by the conflict dialog are not tracked by Undo.
- One orb (multi-monitor placement works; one wheel instance).

## License

[MIT](LICENSE) © Ivan Larin
