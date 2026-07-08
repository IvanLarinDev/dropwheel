# Changelog

## v0.10.0 - 2026-07-08

### Features

- Add selectable wheel open animations with a compact Settings control.
- Add animation speed control for target reveal motion.

### Fixes

- Keep targets closed when Alt is already held to drag the orb.

## v0.9.0 - 2026-07-08

### Features

- Add per-target launch options for executable and script targets.
- Add approved mockups for the per-target launch options editor.

### Fixes

- Harden config saves with local logging and a clearer error path.
- Log launch failures instead of swallowing them silently.
- Edit routing rules on deep copies to avoid watcher-side mutation races.

### Notes

- Launch customization now belongs to each target. Global launch command editing was intentionally removed before release.
