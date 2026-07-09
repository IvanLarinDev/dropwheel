# Changelog

- - -
## [v0.10.2](https://github.com/IvanLarinDev/dropwheel/compare/9891c2ff92ba5a1852aeb8f40967c4825cc5c75a..v0.10.2) - 2026-07-09
#### Bug Fixes
- (**files**) handle partial undo safely - ([b94f752](https://github.com/IvanLarinDev/dropwheel/commit/b94f752f124983f748447695f51f249511e99193)) - Ivan Larin
- (**files**) harden drop safety paths - ([5bb7b09](https://github.com/IvanLarinDev/dropwheel/commit/5bb7b09d86779f86ad8223953d329872aaee3d2d)) - Ivan Larin
#### Miscellaneous Chores
- (**format**) normalize product code formatting - ([0a2692a](https://github.com/IvanLarinDev/dropwheel/commit/0a2692ad5d49aaf1df0f1ec8d030b978bc89426e)) - Ivan Larin
- (**release**) restore changelog remote context - ([63d835c](https://github.com/IvanLarinDev/dropwheel/commit/63d835c475fdb6e4ccfcb206afef8c8e5d3ab54f)) - Ivan Larin

- - -

## [v0.10.1](https://github.com/IvanLarinDev/dropwheel/compare/241caf59ad9b8133fb72717c0242ca9d1d493164..v0.10.1) - 2026-07-08
#### Bug Fixes
- (**files**) harden drop operations (#10) - ([8d1be7a](https://github.com/IvanLarinDev/dropwheel/commit/8d1be7ade82b5462b3be483903d99d3a112b8043)) - Ivan Larin
- (**harness**) close rollout gates (#9) - ([621c07e](https://github.com/IvanLarinDev/dropwheel/commit/621c07e69c1bd99d8425248df5bf87c7885aca6d)) - Ivan Larin
#### Documentation
- (**readme**) simplify interface diagram - ([4c9ba54](https://github.com/IvanLarinDev/dropwheel/commit/4c9ba547804653c53d7e95148fa7276c5e12bd20)) - Ivan Larin
- (**readme**) add annotated interface diagram - ([ac2fec3](https://github.com/IvanLarinDev/dropwheel/commit/ac2fec3a5ec9f43de53d2c1fbee2d9892eecd278)) - Ivan Larin
- (**readme**) document interface element names - ([241caf5](https://github.com/IvanLarinDev/dropwheel/commit/241caf59ad9b8133fb72717c0242ca9d1d493164)) - Ivan Larin
#### Miscellaneous Chores
- (**harness**) bootstrap dev loop checks - ([e8acfb1](https://github.com/IvanLarinDev/dropwheel/commit/e8acfb1b260f2998924245596810f48abac307ba)) - Ivan Larin

- - -


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
