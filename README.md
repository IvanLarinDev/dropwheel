# Dropwheel

[![Release](https://img.shields.io/github/v/release/IvanLarinDev/dropwheel)](https://github.com/IvanLarinDev/dropwheel/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

<p align="center">
  <img src="docs/wheel.svg" alt="Dropwheel — the radial wheel open on the desktop with target tiles around a central orb" width="500">
</p>

Overlay launcher for Windows 10/11: a floating orb that expands into a radial
**wheel** of targets (folders and apps). Drop files to copy or move them, drop
**text** from a browser or editor to save it as a file, or route files into
subfolders with **sorter rules**. The wheel opens by itself when a drag
approaches the orb.

## In action

The interface comes to life in an interactive demo, rendered live in your
browser — no video files:

<p align="center">
  <a href="https://ivanlarindev.github.io/dropwheel/demo/"><b>▶ Open the live demo</b></a>
  &nbsp;·&nbsp;
  <a href="https://ivanlarindev.github.io/dropwheel/demo/index.en.html">English</a>
  &nbsp;/&nbsp;
  <a href="https://ivanlarindev.github.io/dropwheel/demo/">Русский</a>
</p>

It replays the wheel opening, dropping files (copy/move badges), the wheel
auto-opening as a drag approaches the orb, entering a group, and saving dropped
text to a file. The demo source lives in [`docs/demo/`](docs/demo/).

## Install

Grab `Dropwheel.exe` from the [latest release](https://github.com/IvanLarinDev/dropwheel/releases/latest)
and run it — a single file that needs the .NET 10 Desktop Runtime installed.
Config lives in `%AppData%\Dropwheel\config.json`.

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
| Drop a file           | drag onto a target tile; the badge names the action — Copy / Move |
| Drop text             | drag selected text onto a folder → saves `text_<date>.txt` (`.md` if it looks like Markdown) |
| Open with an app      | drag files onto an .exe/.bat/.ps1/… target → runs it with them as arguments |
| Force copy / move     | hold Ctrl / Shift while dropping on a target tile   |
| Undo last action      | click “Undo” in the toast — covers file drops, adds, sorts, and deleting a target |
| Edit a target         | right-click its tile                               |
| Add a target          | drop a folder/exe/link onto the “+” tile or the orb |
| Pin a target from the desktop | Alt+Shift-drag the orb onto a folder, app or file in Explorer or on the desktop — it’s pinned first, next to the hub |
| Create a group        | right-click the orb → “New group…”                 |
| Enter a group         | click its tile, or hover it for 0.5 s while dragging |
| Enter by group code   | hover the orb and type the group's one- or two-digit badge |
| Sort a sorter now     | middle-click a sorter tile                         |
| Reorder tiles         | left-drag a target tile onto another tile; drop on “+” to move it last |
| Move the orb          | Alt + left-drag (any monitor); add Shift to pin instead |
| Wheel at cursor       | Ctrl+Alt+Space (configurable)                      |
| Settings              | tray icon or orb context menu                      |

The orb hides automatically in full-screen apps (games, presentations) — while
it's hidden the tray menu shows an *Orb hidden — fullscreen app active* status so
you know where it went — and it can fade out when idle (see Settings).

Turn on **Skip duplicate targets** in Settings and dropping a folder, app or link
that already sits on the current wheel level won't add a second tile — a toast says
it's already there and the existing tile gives a quick pulse so you can spot it. The
same folder can still live on different group levels. Off by default.

Group codes stay attached to their groups when tiles are reordered. If both `1`
and `11` exist, `1` opens after the configurable sequence timeout, while typing
the second `1` before that timeout opens `11` immediately. Right-click a group
to edit or disable its code.

## Feedback, dialogs & broken targets

Every action reports back in the same place: a short toast on the wheel names what
happened in plain words — *Copied*, *Moved*, *Sorted*, *Undone* — with an **Undo**
link where it applies. All the windows (new group, target editor, settings, and a
themed message box) share one frame and one button layout, follow the active theme,
and validate **inline** instead of popping up error dialogs; the hotkey field checks
itself as you type, and settings are grouped into sections. If a target's folder or
file goes missing, its tile shows a small warning mark — not a silent fade — and a
click offers **Locate…** or **Remove**.

## A living interface

The wheel reacts before you click it. As a drag approaches the orb, a halo glows,
the core breathes and leans toward the cursor, and at the threshold the wheel
unfolds on its own — the inhale flowing straight into the reveal. During an
**Alt+Shift** capture the ghost orb arms as it nears a valid target: its core
fills with the accent colour, a frame lights around the object in Explorer or on
the desktop, and a radar aura pings outward while it holds the lock. On release
the ghost furls back into the orb with a confirming ring, and the wheel opens with
the freshly pinned tile first. Every one of these moments plays live in the
[interactive demo](https://ivanlarindev.github.io/dropwheel/demo/).

## Crowded levels (overflow)

A level with only a few targets always draws as the classic single ring. When a
level fills up you can let the surplus flow onto a second, outer ring instead of
cramming everything onto one rim. The layout is chosen in Settings:

- **None** — the classic wheel: every tile stays on one ring, however many there
  are. No second ring ever appears. This is the default, so existing wheels are
  unchanged.
- **Split balanced** — tiles split evenly across two equal-size rings.
- **Overflow band** — the inner ring stays the familiar single wheel up to its
  cap; only the surplus lands on a new outer band, staggered into the gaps.
- **Petals** — tiles alternate between rings, the outer row offset into the gaps
  — the most compact layout.
- **Columns** — tiles form radial pairs on shared spokes (inner + outer per column).

The **threshold** setting is how many real targets a level holds before the
second ring appears, clamped to 4–16. The always-present tiles — the "+" add tile
and, inside a group, "Back" — don't count toward it, so they never push a level
into overflow on their own.

## Interface names

Use these names when describing the wheel in issues, docs, or UI changes. The
[interactive demo](https://ivanlarindev.github.io/dropwheel/demo/) shows the same
parts numbered on a live wheel.

| Name | Meaning |
|------|---------|
| **Orb** | The small floating Dropwheel button that sits on the desktop when the wheel is closed. |
| **Wheel** | The expanded radial overlay that opens around the orb. |
| **Hub** | The center of the open wheel; dropping on it follows the same add-target behavior as dropping on the orb. |
| **Rim** | The outer circular band that target tiles sit around. |
| **Spokes** | The radial guide lines from the hub to each tile. |
| **Target tile** | A square drop destination on the rim, such as Downloads, Documents, Desktop, or Pictures. |
| **Tile label** | The text under a tile, for example `Downloads` or `Pictures`. |
| **Add tile** | The dashed `+` tile used to create a new target in the current level. |
| **Folder target** | A target tile backed by a folder path; dropped files are copied or moved into it. |
| **Run target** | A target tile backed by an executable or script; dropped files are passed to it as arguments. |
| **Sorter target** | A folder target with routing rules, shown with an amber border and a `Sort` badge. |
| **Group target** | A target tile that opens another wheel level instead of receiving files directly. |
| **Badge** | The small marker on a tile that names an action or state in plain words — `Copy`, `Move`, `Sort`, `Run` — plus a group's shortcut number. |

## Sorter targets & routing rules

A **sorter** distributes dropped files into subfolders by rules (amber-bordered
tile, `Sort` badge). Right-click a target and press **Convert to routing rules**, or
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

**Token destinations** — a rule's destination can lift fields out of the file name
with `${name}` placeholders. Add a **Name regex** condition with named groups, e.g.
`(?<ep>ep\d+)_(?<sq>sq\d+)_(?<sh>sh\d+)`, and set the destination to
`episodes\${ep}\${sq}\${sh}`; a file `ep001_sq001_sh001_playblast_v001.mov` then lands
in `episodes\ep001\sq001\sh001\`. Tokens fill from the rule's own Name regex groups, so
one pattern both matches and routes, and the editor lists the tokens a rule can fill
right under the destination box. A file that matches the rule but leaves a token empty
goes to the target root rather than into a half-built path.

**Watch a folder** — a folder sorter can watch itself. Tick **Watch folder, auto-sort
new files** in the rule editor and Dropwheel routes files that appear in the folder by
the same rules, in the background. When watch is enabled, Dropwheel immediately sweeps
existing top-level files once, then keeps watching for new arrivals. It waits for a file
to finish copying before moving it, watches only the top level (files routed into
subfolders don't re-trigger it), and leaves a file in place when no rule matches. A tray
notification reports how many files were sorted. Auto-sort **moves** files and is **not**
tracked by Undo — revert from Explorer if needed.

The legacy extension map still loads and is migrated to rules on first edit:

    { "Name": "Sort", "Path": "D:\\Sorted",
      "SortRules": { "jpg png webp": "Images", "pdf docx": "Docs", "*": "Other" } }

Undo reverts the whole batch.

## Text drops

Drag selected text from a browser, editor, or chat onto a folder tile and
Dropwheel writes it to `text_YYYY-MM-DD_HH-mm-ss.txt` — or `.md` when the text
looks like Markdown (headings, code fences, links). Dropped on a sorter, the new
file is routed by the rules; Undo removes it.

## Run targets (open with)

If a target is an executable or script (`.exe`, `.com`, `.bat`, `.cmd`, `.ps1`,
`.py`, `.pyw`, `.vbs`, `.wsf`, `.js`, `.jar`, or a `.lnk` to one), dropping files
on its tile runs it with the dropped files as arguments — the Windows "open with"
behaviour, shown with a `Run` badge. Scripts the shell would only open in an editor
(`.ps1`, `.py`, `.jar`) are launched through their interpreter. This is a launch,
not a file operation, so it isn't undoable.

## Link targets

Drop a link such as `https://example.com`, `tg://resolve?domain=telegram`, or
`https://t.me/c/4379453334/1` onto the orb or the “+” tile to create a
quick-access target. Browser URL drags keep the page title when the drag payload
includes one, and Dropwheel fetches a favicon in the background when the page
exposes a PNG/JPG/ICO/WEBP icon.

Telegram web links are converted to desktop deep links where possible, so
clicking a `t.me` tile opens Telegram Desktop when the `tg://` protocol is
registered. Dropping files or selected text onto a Telegram tile copies the
payload to the clipboard, opens the chat or topic, and pastes it once Telegram is
foreground; review and press Send in Telegram.

## Themes

Four themes — chosen in Settings. Each carries a full palette: the wheel, the
target editor and settings windows, the orb context menu, and the tray menu all
follow the theme (accent colour, surfaces, text); group and sorter tile borders
are tuned per theme. Switch themes live in the
[demo](https://ivanlarindev.github.io/dropwheel/demo/) to compare Fluent, Dark,
Light and Neon.

## Project layout

    src/Dropwheel/
      Models/    TargetItem, AppConfig, SortRule (conditions), FilePreset
      Services/  TargetStore (JSON config), FileOps (SHFileOperation),
                 VirtualFileService, TextDropService, SortService, SortMigration,
                 WatcherService (auto-sort watched folders), FileMeta, PresetService,
                 ShortcutResolver, MouseHook, HotkeyService, LaunchService,
                 IconService, LinkTargetService, LinkMetadataService,
                 TelegramDropService, StartupService, FullscreenDetector
      UI/        OverlayWindow (hub + rim + spokes wheel, partial classes),
                 DialogShell (shared dialog frame) + DwMessageBox + ToastHost,
                 TargetEditorWindow (+ .Rules master-detail),
                 SettingsWindow (two-pane sections),
                 Themes, Palette (colour roles), MenuTheme.xaml
    tests/       Dropwheel.Tests (xUnit: SortService, SortMigration, FileMeta,
                 TextDropService, WatcherService, HotkeyService, VirtualFileService,
                 LinkTargetService, LinkMetadataService, TelegramDropService)
    docs/demo/   live interactive JS demo (GitHub Pages)

## Known limitations

- Dragging from elevated (admin) processes does not work — Windows UIPI.
- Virtual files (Outlook attachments, browser drags) and dropped text are always
  copied; files renamed by the conflict dialog are not tracked by Undo.
- One orb (multi-monitor placement works; one wheel instance).

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to
propose changes, [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for community
expectations, and [SECURITY.md](SECURITY.md) for reporting vulnerabilities
privately. Bug reports and feature requests use the issue templates.

## License

[MIT](LICENSE) © Ivan Larin
