# Changelog

- - -
## [v0.12.0](https://github.com/IvanLarinDev/dropwheel/compare/647c2ca2c4eb3981b191d0f87277f725d693e1a2..v0.12.0) - 2026-07-10
#### Features
- (**overlay**) enrich browser URL targets - ([b387999](https://github.com/IvanLarinDev/dropwheel/commit/b3879996c89fef78c32f6dffe94a84848cebe121)) - Ivan Larin
- (**overlay**) stage telegram drops on clipboard - ([75947d7](https://github.com/IvanLarinDev/dropwheel/commit/75947d79dfc2d3fe319c8d6ab9643687401d23ee)) - Ivan Larin
- (**overlay**) add link quick-access targets - ([b60c4fc](https://github.com/IvanLarinDev/dropwheel/commit/b60c4fc0d0543c88bbee7509fdfc1ad9314104d4)) - Ivan Larin
#### Bug Fixes
- (**config**) merge unknown enum config load - ([0aa30ee](https://github.com/IvanLarinDev/dropwheel/commit/0aa30eeb3352c64beb9d6c937d44bab4a60c608e)) - Ivan Larin
- (**config**) preserve config on unknown enum load - ([fe9fad2](https://github.com/IvanLarinDev/dropwheel/commit/fe9fad2f2a4a1b21cd702c50ea5cb48a3580238b)) - Ivan Larin
- (**overlay**) accept delayed telegram text drops - ([a32c791](https://github.com/IvanLarinDev/dropwheel/commit/a32c7918b73e0160fcab218715182557f8a7de4e)) - Ivan Larin
- (**overlay**) accept move-only telegram text drops - ([a247351](https://github.com/IvanLarinDev/dropwheel/commit/a247351e92916d6c80f992508ae5f839d3a4ffdc)) - Ivan Larin
- (**overlay**) paste telegram drops into topic - ([c3e3b05](https://github.com/IvanLarinDev/dropwheel/commit/c3e3b05bc1b34240150604ea29b9b23165076907)) - Ivan Larin
- (**overlay**) open telegram links in desktop app - ([599a833](https://github.com/IvanLarinDev/dropwheel/commit/599a833f1ef07869a27105e56f6e08aada15cbf5)) - Ivan Larin
- (**overlay**) use compatible add-target drag effect - ([1f888da](https://github.com/IvanLarinDev/dropwheel/commit/1f888dab7ce575311ce1a9b2a47eae59ff219ccd)) - Ivan Larin
- (**overlay**) accept bare telegram links - ([0f80897](https://github.com/IvanLarinDev/dropwheel/commit/0f808971a0c97a3993a814e0d6dc2c3a07973860)) - Ivan Larin
- (**overlay**) handle saved messages chat drops - ([82c26fb](https://github.com/IvanLarinDev/dropwheel/commit/82c26fb650a0b8ee894d4b98e7b5793f8c18cdf3)) - Ivan Larin
- (**overlay**) prioritize link drops over text saves - ([401b20d](https://github.com/IvanLarinDev/dropwheel/commit/401b20d2762591f29a4b979da9ff3aadc368b19d)) - Ivan Larin
- (**overlay**) normalize virtual sorter roots - ([cee2d28](https://github.com/IvanLarinDev/dropwheel/commit/cee2d282dca3dd696536873882fe488f05210c47)) - Ivan Larin
- (**sorter**) skip same-folder drop operations - ([c0e10b1](https://github.com/IvanLarinDev/dropwheel/commit/c0e10b1ee11d8f3d255f4b503356bca106df55d1)) - Ivan Larin
- (**text**) avoid directory-name drop collisions - ([a5571a1](https://github.com/IvanLarinDev/dropwheel/commit/a5571a1f646e5a975963cf087c5a1250ce204ad2)) - Ivan Larin
- (**watcher**) gate queued sort after stop - ([98f7985](https://github.com/IvanLarinDev/dropwheel/commit/98f7985fb7eacefc66a6edf861ac7c853fad67a4)) - Ivan Larin
- (**watcher**) cancel queued work on stop - ([9eceb44](https://github.com/IvanLarinDev/dropwheel/commit/9eceb44134b457582fac717c11a87dd469b6049a)) - Ivan Larin
#### Documentation
- (**readme**) document link and telegram targets - ([b939ed2](https://github.com/IvanLarinDev/dropwheel/commit/b939ed2e02146a37c97192ffe516ee76506b4143)) - Ivan Larin
#### Tests
- (**watcher**) add autosort collision regression - ([87768e3](https://github.com/IvanLarinDev/dropwheel/commit/87768e362170c0bb5d0b63159be91610a8871c3f)) - Ivan Larin
#### Miscellaneous Chores
- (**dropwheel**) merge telegram quick access target - ([32724a2](https://github.com/IvanLarinDev/dropwheel/commit/32724a29a90136c71fb216be7bebce76b2c7341e)) - Ivan Larin
- (**dropwheel**) merge watcher collision test - ([afa0552](https://github.com/IvanLarinDev/dropwheel/commit/afa0552c6b31a2a41e1f7c7aa85c2bac5d4b3e4d)) - Ivan Larin
- (**dropwheel**) merge overlay root fix - ([32e6373](https://github.com/IvanLarinDev/dropwheel/commit/32e637324d1ad0c5560fdbee6d191896d9f8d559)) - Ivan Larin
- (**dropwheel**) merge watcher stop race guard - ([f9d1735](https://github.com/IvanLarinDev/dropwheel/commit/f9d17355e50e4f3db348fd6105ff5132d14e56cf)) - Ivan Larin
- (**dropwheel**) merge watcher stop fix - ([9595985](https://github.com/IvanLarinDev/dropwheel/commit/9595985328b4f85593e76ee90d248ece32b82093)) - Ivan Larin
- (**dropwheel**) merge text drop collision fix - ([f279424](https://github.com/IvanLarinDev/dropwheel/commit/f279424c87b0bb71821913347c87e7a7a244e173)) - Ivan Larin
- (**dropwheel**) merge verified harness and sorter fixes - ([e57dc51](https://github.com/IvanLarinDev/dropwheel/commit/e57dc512cd7d4754aeb6f87bb5db3b699c3190f3)) - Ivan Larin
- (**harness**) update generated harness - ([647c2ca](https://github.com/IvanLarinDev/dropwheel/commit/647c2ca2c4eb3981b191d0f87277f725d693e1a2)) - Ivan Larin
- (**version**) set project version v0.12.0 - ([d3c2e92](https://github.com/IvanLarinDev/dropwheel/commit/d3c2e92e58e4f30d3ce2cac986d591874659f402)) - Ivan Larin

- - -

## [v0.11.0](https://github.com/IvanLarinDev/dropwheel/compare/d6ab8bfd1380a84d043c5da4e76865ca14a07456..v0.11.0) - 2026-07-09
#### Features
- (**overlay**) animate tile reorder - ([84916d1](https://github.com/IvanLarinDev/dropwheel/commit/84916d16712bba456b5ae042f32d4b9d456180f5)) - Ivan Larin
- (**overlay**) persist tile order and trigger sorting - ([a107084](https://github.com/IvanLarinDev/dropwheel/commit/a107084b9542a74f59258dc0a46438d7dfb9ea44)) - Ivan Larin
#### Bug Fixes
- (**overlay**) preview tile reorder during drag - ([719248c](https://github.com/IvanLarinDev/dropwheel/commit/719248c4e21ab49ed69c81e89d21fa34deb26ed4)) - Ivan Larin

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
