# UI — windows and wheel

This directory contains the complete Dropwheel interface: the floating orb,
expandable target wheel, settings window, target and rule editor, and compact
hints. Non-UI behavior belongs in `../Services`; data belongs in `../Models`.

## Contents

- `OverlayWindow` — the main transparent window containing the orb and wheel.
  Because it is large, it is split into focused `OverlayWindow.*.cs` partial
  files: layout (`Layout`), tile cloud (`Cloud`), tiles (`Bubble`), drag and drop
  (`Dnd`, `OrbDrop`, `PinDrop`), proximity response (`Proximity`, `Charge`),
  Alt+Shift capture (`Capture`), groups and shortcuts (`GroupShortcuts`), undo
  (`Undo`), idle fading (`IdleFade`), and others.
- `SettingsWindow` — the settings window, with sections on the left, live field
  validation, and hotkeys selected through chips or recording rather than typed
  manually.
- `TargetEditorWindow` — the target and sorting-rule editor
  (`TargetEditorWindow.Rules.cs`), including per-tile emoji and color settings.
- `PromptWindow` — simple text input.
- `DialogShell`, `DwMessageBox`, `ToastHost` — the shared dialog frame, themed
  system message-box replacement, and wheel feedback toast.
- `Themes`, `Palette`, `MenuTheme.xaml` — colors and presentation.

## Working with the UI

The main window is a `partial class OverlayWindow`. Put a new concern in a new
`OverlayWindow.<Topic>.cs` file instead of expanding an existing one. Geometry,
color, and timing values are the source of truth for the live demo in
`docs/demo`; update the demo when those values change.

## Guidelines

- Visual, animation, and layout changes require mockups through the project's
  design gate. A logic-only UI change is allowed, but the push gate still
  requires a waiver explaining that no visuals changed.
- Do not place file, network, or process-launching business logic here; it
  belongs in `../Services`.
- Tiles use simple shapes, so set `AutomationProperties.Name` or screen readers
  will not announce them.
