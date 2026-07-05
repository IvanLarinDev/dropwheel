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
common loops: `run.cmd [run|build|publish|stop]`.

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
fade out when idle (see Settings). Themes: Fluent, Dark, Light, Neon.

## Sorter targets

A target with `SortRules` distributes dropped files into subfolders
(amber-bordered tile, ⇅ badge):

    { "Name": "Sort", "Path": "D:\\Sorted",
      "SortRules": { "jpg png webp": "Images", "pdf docx": "Docs", "*": "Other" } }

Keys are space-separated extensions, `*` catches the rest. Values are
subfolders relative to `Path` or absolute paths. Undo reverts the whole batch.

## Project layout

    src/Dropwheel/
      Models/    TargetItem, AppConfig
      Services/  TargetStore (JSON config), FileOps (SHFileOperation),
                 VirtualFileService, SortService, MouseHook, HotkeyService,
                 LaunchService, IconService, StartupService, FullscreenDetector
      UI/        OverlayWindow (hub + rim + spokes wheel, partial classes),
                 TargetEditorWindow, SettingsWindow, Themes
    docs/        concept notes

## Known limitations

- Dragging from elevated (admin) processes does not work — Windows UIPI.
- Virtual files (Outlook attachments, browser drags) are always copied;
  files renamed by the conflict dialog are not tracked by Undo.
- One orb (multi-monitor placement works; one wheel instance).

## License

[MIT](LICENSE) © Ivan Larin
