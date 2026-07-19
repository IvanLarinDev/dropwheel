# Models — application data

This directory contains the simple data classes and enums that describe
Dropwheel state: targets, settings, and sorting rules. It defines what the
application stores, without behavioral logic.

## Contents

- `AppConfig` — the complete user configuration: orb position and opacity, both
  hotkeys (the primary hotkey and the optional "open at orb" hotkey), theme,
  toast duration and sound, wheel overflow mode, targets, and rule presets. This
  object is stored in `config.json`.
- `TargetItem` — one wheel tile: a folder, application, link, or group (groups
  have `Children`). It also contains helper properties such as `IsGroup`,
  `IsSorter`, and `IsFolder`, the `Rules` collection, and tile presentation data
  such as an emoji and custom color.
- `SortRule` — a conditional (`RuleCondition`) rule that routes a file into a
  subfolder.
- `OpenAnimation`, `OverflowLayout` — the available opening animations and
  overflow layouts.
- `FilePreset` — a reusable extension set for the rule editor.

## Working with models

These classes are serialized to JSON, so property names are part of the on-disk
file format. Services in `../Services`, primarily `TargetStore`, read and write
them; `../UI` renders them.

## Guidelines

- New properties and enum values are allowed, but renaming a property breaks old
  user `config.json` files. Add new enum values to the sanitizer in `TargetStore`
  so an unknown value cannot erase the entire configuration.
- Do not put behavioral logic, file operations, networking, or UI code here.
  Keep this layer to data and small computed properties over that data.
