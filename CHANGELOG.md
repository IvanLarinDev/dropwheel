# Changelog

- - -
## [v0.26.1](https://github.com/IvanLarinDev/dropwheel/compare/v0.26.0..v0.26.1) - 2026-07-19
#### Bug Fixes
- (**release**) preserve pipeline and UTF-8 changelog (#39) - ([b36ec8c](https://github.com/IvanLarinDev/dropwheel/commit/b36ec8c166c100ab8bc1f847d6dce8bc200c50eb)) - Ivan Larin

- - -
## [v0.26.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.25.0..v0.26.0) - 2026-07-19
#### Miscellaneous Chores
- integrate milestones for v0.26.0 (#37) - ([6428e47](https://github.com/IvanLarinDev/dropwheel/commit/6428e475c33ad24e5821b638babac934e4c2b59f)) - Ivan Larin
- (**deps**) bump actions/setup-dotnet in the github-actions group (#33) - ([6eb2eeb](https://github.com/IvanLarinDev/dropwheel/commit/6eb2eeb41ca788e45309c7e39c4909bd26e57557)) - dependabot\[bot\]
- pin reproducible .NET toolchain (#32) - ([82f08b4](https://github.com/IvanLarinDev/dropwheel/commit/82f08b4486b5247c17c32fefe4abb07e34cae3e1)) - Ivan Larin
- (**git**) clean up the repository structure - ([0f03293](https://github.com/IvanLarinDev/dropwheel/commit/0f032932029a1332648a9dcc5ae3c6e53e147f65)) - Ivan Larin
#### Other Changes
- \[verified\] fix: harden watcher stability and Windows smoke coverage (#31) - ([fc7dc9c](https://github.com/IvanLarinDev/dropwheel/commit/fc7dc9c6e58c59d73f804bc76f5254df1aa1b368)) - Ivan Larin

- - -
## [v0.25.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.24.1..v0.25.0) - 2026-07-15
#### Features
- (**ui**) add a live opening-animation preview in Settings - ([b8583bf](https://github.com/IvanLarinDev/dropwheel/commit/b8583bfe2e084304987c85b33a606efa90a7db30)) - Ivan Larin

- - -
## [v0.24.1](https://github.com/IvanLarinDev/dropwheel/compare/v0.24.0..v0.24.1) - 2026-07-15
#### Bug Fixes
- (**test**) make test temp-folder cleanup resilient to the error.log flake - ([7cafa5e](https://github.com/IvanLarinDev/dropwheel/commit/7cafa5eb5cf25b2d7c856f0ca06ca06f8e9cc63b)) - Ivan Larin

- - -
## [v0.24.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.23.0..v0.24.0) - 2026-07-15
#### Features
- (**ui**) create groups through the Add tile with a Target/Group switch - ([9c4b04a](https://github.com/IvanLarinDev/dropwheel/commit/9c4b04ad395cabf51a81e6f25a0f76027557b4c8)) - Ivan Larin
- (**ui**) make the target editor resizable with sorter-style splitters - ([12f0bdc](https://github.com/IvanLarinDev/dropwheel/commit/12f0bdca9e27a852cdf748ad83755b252f957689)) - Ivan Larin

- - -
## [v0.23.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.22.0..v0.23.0) - 2026-07-15
#### Features
- (**ui**) add a Wheel size slider in Settings for 0.8–1.5 scaling - ([d385672](https://github.com/IvanLarinDev/dropwheel/commit/d385672b74758070dc5c1c39770ffcb9b2d02534)) - Ivan Larin
- (**ui**) add wheel-scaling plumbing with WheelScale and ScaleTransform (unchanged at 1.0) - ([4462e23](https://github.com/IvanLarinDev/dropwheel/commit/4462e230d04011e9d953384ed9d0f5d048ad6156)) - Ivan Larin

- - -
## [v0.22.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.21.1..v0.22.0) - 2026-07-15
#### Features
- (**ui**) add a Magnetic settle animation with an arc approach and spring snap - ([a4ef2c8](https://github.com/IvanLarinDev/dropwheel/commit/a4ef2c8ca8ecb815392bd0e6e28f55e5f4b7a050)) - Ivan Larin
- (**ui**) document Destination tokens, ${stem:N} formats, and group padding - ([0c96f3a](https://github.com/IvanLarinDev/dropwheel/commit/0c96f3a849e1280998899bed70f0033cc8b83379)) - Ivan Larin
#### Bug Fixes
- (**ui**) remove the gap above the rule panel and scrollbar overlap in the target editor - ([969783f](https://github.com/IvanLarinDev/dropwheel/commit/969783f0e0b09c85d15feb4f9fe70a0320771cd0)) - Ivan Larin

- - -
## [v0.21.1](https://github.com/IvanLarinDev/dropwheel/compare/v0.21.0..v0.21.1) - 2026-07-14
#### Bug Fixes
- (**ui**) let the hover chip extend beyond the tile grid instead of clipping it - ([69871ef](https://github.com/IvanLarinDev/dropwheel/commit/69871effbe2e54f4dcf8e1051b224e9f16bdae93)) - Ivan Larin

- - -
## [v0.21.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.20.0..v0.21.0) - 2026-07-14
#### Features
- (**ui**) show the folder item count in the hover chip - ([b2d26b5](https://github.com/IvanLarinDev/dropwheel/commit/b2d26b560cdf972a7d4fdd9721337347ac785625)) - Ivan Larin
#### Bug Fixes
- (**config**) make saves survive a transient config.json lock - ([6e65d3c](https://github.com/IvanLarinDev/dropwheel/commit/6e65d3cb0ae50e961bff0186afee27f2748127bc)) - Ivan Larin
- (**ui**) fit both location and count lines in the hover chip - ([a294ccb](https://github.com/IvanLarinDev/dropwheel/commit/a294ccb64e419dd95588d7d232603621154388a8)) - Ivan Larin
- (**sort**) apply sorting pauses to text, virtual files, and SendTo - ([8447d5f](https://github.com/IvanLarinDev/dropwheel/commit/8447d5f5f784b720fc5923d0ec9409827afc191a)) - Ivan Larin
- (**release**) resume after a tag push failure and retry network operations - ([ecbaca1](https://github.com/IvanLarinDev/dropwheel/commit/ecbaca172228a692e95c4c6437b5dba2b9e34aa9)) - Ivan Larin
#### Documentation
- (**demo**) add emoji and color tiles, a second chip line, and new-feature cards - ([e0dbc06](https://github.com/IvanLarinDev/dropwheel/commit/e0dbc0691f9330bc7512a008ed312a9d80c47f94)) - Ivan Larin
- bring documentation up to the v0.20.0 feature set and add README/LLM files for tests and scripts - ([3a83533](https://github.com/IvanLarinDev/dropwheel/commit/3a83533916bea4e13bedd69932dfd1c324303243)) - Ivan Larin
#### Miscellaneous Chores
- eliminate all compiler warnings - ([0a27aea](https://github.com/IvanLarinDev/dropwheel/commit/0a27aea30efa330cd436a929fc30966756be81b3)) - Ivan Larin

- - -
## [v0.20.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.19.0..v0.20.0) - 2026-07-14
#### Features
- (**tray**) add menu-item icons, accent check marks, and menu grouping - ([0531ccd](https://github.com/IvanLarinDev/dropwheel/commit/0531ccd53fa4c4967e271cc2453a8318019d3fdc)) - Ivan Larin
- (**tray**) export settings and reload configuration from disk - ([71b6ef2](https://github.com/IvanLarinDev/dropwheel/commit/71b6ef291e970bee812893bf05d290ee4d1896fb)) - Ivan Larin
- (**ui**) select hotkeys with chips instead of manual input - ([df9d8ea](https://github.com/IvanLarinDev/dropwheel/commit/df9d8ea578b5cc635117f5b00526a8d2bc527c7e)) - Ivan Larin
- (**ui**) add a second hotkey to open the wheel at the orb - ([0c12013](https://github.com/IvanLarinDev/dropwheel/commit/0c120133271a14c86e68f00d5cdfa60cffe012ef)) - Ivan Larin
- (**ui**) make toast duration and sound configurable - ([448ef55](https://github.com/IvanLarinDev/dropwheel/commit/448ef55c3dd165ad8d1f94e5635d370df55fe1a7)) - Ivan Larin
- (**tray**) add separate pause controls for auto-sort and all sorting - ([1b230cc](https://github.com/IvanLarinDev/dropwheel/commit/1b230cc3ae36d141d00e207078e6dc2cc240f9f3)) - Ivan Larin
- (**tray**) pause auto-sort from the tray - ([8b44907](https://github.com/IvanLarinDev/dropwheel/commit/8b4490765030260b5a8dc18e09fe8e2cb3e4e814)) - Ivan Larin
- (**history**) copy recent-drop history to the clipboard - ([f431838](https://github.com/IvanLarinDev/dropwheel/commit/f431838d8ac706e9f1b00277d574a582965c14c6)) - Ivan Larin
- (**sort**) report the folder count in sorter toasts - ([5699473](https://github.com/IvanLarinDev/dropwheel/commit/5699473d8ec8ff40059254a61cc16836bddebf75)) - Ivan Larin
- (**ui**) show free disk space in the hover chip - ([8d2419c](https://github.com/IvanLarinDev/dropwheel/commit/8d2419cab4dd0058628d45edf347a3d1d14e0850)) - Ivan Larin
- (**ui**) add a custom tile-border color - ([5d8f0a4](https://github.com/IvanLarinDev/dropwheel/commit/5d8f0a4990a1632ed94557c7a37e441d709ee625)) - Ivan Larin
- (**ui**) use a custom emoji on a tile instead of its icon - ([11f2bd7](https://github.com/IvanLarinDev/dropwheel/commit/11f2bd73f3522f9a360a5abf24d22c83ad036af5)) - Ivan Larin
- (**ui**) show the full target path in the tile tooltip - ([4e7dce1](https://github.com/IvanLarinDev/dropwheel/commit/4e7dce181f7dd620281998f16547c5bd6e3e996c)) - Ivan Larin
#### Bug Fixes
- (**ui**) improve dark tray-menu highlighting and tile emoji color readability - ([a52c5c6](https://github.com/IvanLarinDev/dropwheel/commit/a52c5c630b8de163bbdbdf52804e22b8a873ba82)) - Ivan Larin
- (**ui**) add spacing between Settings content and the scrollbar - ([f598f33](https://github.com/IvanLarinDev/dropwheel/commit/f598f33f16631bf01d773318252306a0ebeb68b1)) - Ivan Larin
- (**sort**) make the sorting pause stop manual tile sorting too - ([f4d87ba](https://github.com/IvanLarinDev/dropwheel/commit/f4d87ba313faf1304df0222993a10e179da4c72d)) - Ivan Larin
- (**tray**) confirm auto-sort pauses with a balloon notification - ([c7e3db7](https://github.com/IvanLarinDev/dropwheel/commit/c7e3db784f843b10ebc41663b8e647709d586841)) - Ivan Larin
- (**history**) copy the list through the WinForms clipboard - ([7d8c577](https://github.com/IvanLarinDev/dropwheel/commit/7d8c577e6a32bb9a85d01a4356f8fec3327c83eb)) - Ivan Larin
- (**ui**) show emoji on group tiles too - ([89fee86](https://github.com/IvanLarinDev/dropwheel/commit/89fee86eb6955f52ebbb46af56ce416b12e332e4)) - Ivan Larin
#### Miscellaneous Chores
- (**design**) add a waiver for the second hotkey at the orb - ([7f19aae](https://github.com/IvanLarinDev/dropwheel/commit/7f19aae283829b9c43e9474882d61576b1c77f44)) - Ivan Larin
- (**design**) add a waiver for toast duration and sound - ([0fa26cd](https://github.com/IvanLarinDev/dropwheel/commit/0fa26cdaf12c9f3065f2c1a51169c934d27fbeda)) - Ivan Larin
- (**design**) add a waiver for pausing manual sorting - ([10ca909](https://github.com/IvanLarinDev/dropwheel/commit/10ca90995bdeb36f1851a6add3c85ce0c8414d15)) - Ivan Larin
- (**design**) add a waiver for the folder count in sorter toasts - ([10854d1](https://github.com/IvanLarinDev/dropwheel/commit/10854d121b42c6a76d2ef2096f9ba2e110cf0849)) - Ivan Larin
- (**design**) add a waiver for free space in the hover chip - ([502ee5c](https://github.com/IvanLarinDev/dropwheel/commit/502ee5cf0117659f5e5a8d7e00e5081ba6672539)) - Ivan Larin
- (**design**) add a waiver for the tile-border color - ([03b557f](https://github.com/IvanLarinDev/dropwheel/commit/03b557fdb6eb3cc69b4794d2af19fa4cf92a711c)) - Ivan Larin
- (**design**) add a waiver for tile emoji - ([feeb15c](https://github.com/IvanLarinDev/dropwheel/commit/feeb15c19928ee8db3c2565c832cb44ff3913305)) - Ivan Larin
- (**design**) add a waiver for the tile path tooltip - ([ca296b6](https://github.com/IvanLarinDev/dropwheel/commit/ca296b65ac2c3146c3401c3c0bee7c311b4eafe7)) - Ivan Larin
- (**release**) fast-forward local main after a release - ([5c5d616](https://github.com/IvanLarinDev/dropwheel/commit/5c5d6161bceba74e450a2bb245e33e8ebd3bcfe1)) - Ivan Larin

- - -
## [v0.19.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.18.0..v0.19.0) - 2026-07-14
#### Features
- (**drop**) let targets choose name-conflict behavior - ([86ab1e5](https://github.com/IvanLarinDev/dropwheel/commit/86ab1e544ce7558c69df2f3503f45acef9053331)) - Ivan Larin
- (**drop**) rename dropped files with a per-target template - ([a316bae](https://github.com/IvanLarinDev/dropwheel/commit/a316bae2cb65ae3fc297db53cfe76c7a5d8153e6)) - Ivan Larin
- (**link**) drop multiple links in one action - ([f9ea3e5](https://github.com/IvanLarinDev/dropwheel/commit/f9ea3e5fb982845d5e411c98f7d3a1db4896d4b0)) - Ivan Larin
- (**drop**) copy the destination path to the clipboard after a drop - ([bd1738b](https://github.com/IvanLarinDev/dropwheel/commit/bd1738ba37ee5b62f7feec200ac51200e3ffdd28)) - Ivan Larin
- (**text**) insert text filename tokens by clicking chips - ([190a0b8](https://github.com/IvanLarinDev/dropwheel/commit/190a0b8be3f637f56b1bd1fa56a54ff8b6bbe587)) - Ivan Larin
- (**sort**) use shared ${...} syntax for text filenames and add the ${slug} token - ([a565481](https://github.com/IvanLarinDev/dropwheel/commit/a5654815322ab5f6b98d29899fadc736d558b108)) - Ivan Larin
- (**text**) add a filename template for saved text - ([31094e8](https://github.com/IvanLarinDev/dropwheel/commit/31094e8ef72c612ecbecc75435d18c97168cab2d)) - Ivan Larin
#### Bug Fixes
- (**drop**) save website text dropped on a folder as a file instead of a link - ([f5f4e22](https://github.com/IvanLarinDev/dropwheel/commit/f5f4e227234002c387cacd1873434cbf13f49c48)) - Ivan Larin
#### Documentation
- document group B features in the README and demo - ([2958666](https://github.com/IvanLarinDev/dropwheel/commit/2958666a9c5a207baf903f4f8611efdad2cebe91)) - Ivan Larin
- document the ${slug} token and shared text filename syntax in the README and demo - ([0858efa](https://github.com/IvanLarinDev/dropwheel/commit/0858efaa8e61f1d8463ed0b5962bc8a66183f595)) - Ivan Larin
#### Miscellaneous Chores
- (**design**) add a waiver for the name-conflict policy - ([b3b0b99](https://github.com/IvanLarinDev/dropwheel/commit/b3b0b998133ea33d0815e72797a4598a949f412b)) - Ivan Larin
- (**design**) add a waiver for the per-target filename template - ([448e060](https://github.com/IvanLarinDev/dropwheel/commit/448e060c5582a6ec864b2e87977da139531ec52d)) - Ivan Larin
- (**design**) add a waiver for dropping multiple links - ([e3fff4c](https://github.com/IvanLarinDev/dropwheel/commit/e3fff4cd0948300f7175ca1bbc369c61097604ed)) - Ivan Larin
- (**design**) add a waiver for copying the destination path - ([8aefa07](https://github.com/IvanLarinDev/dropwheel/commit/8aefa07bfa17df3fee34558da94ab1b7b6591670)) - Ivan Larin
- (**design**) add a waiver for text filename token chips - ([460ff83](https://github.com/IvanLarinDev/dropwheel/commit/460ff83db664278f436bf4cb705137534971a686)) - Ivan Larin
- (**design**) add a waiver for shared token syntax and ${slug} - ([119c09c](https://github.com/IvanLarinDev/dropwheel/commit/119c09c95567fbb293504d2c90e5fd0b322b9d40)) - Ivan Larin
- (**design**) add a waiver for prioritizing text over links during drops - ([32e5129](https://github.com/IvanLarinDev/dropwheel/commit/32e5129f45738515a1316dc7df60fc618bb359df)) - Ivan Larin
- (**design**) add a waiver for text-drop filename settings - ([ab8ac2c](https://github.com/IvanLarinDev/dropwheel/commit/ab8ac2c832ee8f745f7594c6dde7f6892f0557f3)) - Ivan Larin

- - -
## [v0.18.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.17.0..v0.18.0) - 2026-07-14
#### Features
- (**sort**) add clickable token chips to the rule editor - ([59c1ffd](https://github.com/IvanLarinDev/dropwheel/commit/59c1ffd38d5bdda6e184edbb29df127a21bb29ed)) - Ivan Larin
- (**sort**) add a media-type condition - ([ec6b79a](https://github.com/IvanLarinDev/dropwheel/commit/ec6b79a826a9465c0b44f72091880624b4625434)) - Ivan Larin
- (**sort**) use familiar comparison symbols instead of Gt/Lt in the editor - ([8e22c0f](https://github.com/IvanLarinDev/dropwheel/commit/8e22c0f630896f0e423aa9c08df946cf00a04586)) - Ivan Larin
- (**sort**) add a file-creation-date condition - ([6112ea5](https://github.com/IvanLarinDev/dropwheel/commit/6112ea5e6464bd07e648a26a7f9283f4f4312ecb)) - Ivan Larin
- (**sort**) add a button to duplicate sorter rules - ([bf977bf](https://github.com/IvanLarinDev/dropwheel/commit/bf977bf3d7d0d9b4c6e199347273e88bf6b5b4e7)) - Ivan Larin
- (**sort**) enable and disable sorter rules - ([3fe1121](https://github.com/IvanLarinDev/dropwheel/commit/3fe11214846e543de62eafa0e491165f8afd4c38)) - Ivan Larin
- (**sort**) add a NOT checkbox to invert rule conditions - ([6866d49](https://github.com/IvanLarinDev/dropwheel/commit/6866d49079b8ebdf14f89c29c8c2a1c4382afcba)) - Ivan Larin
- (**sort**) add configurable size buckets to the size token - ([c1bc287](https://github.com/IvanLarinDev/dropwheel/commit/c1bc287c131cc8d1d4fa330137d92aefae3b5880)) - Ivan Larin
- (**sort**) add the sizebucket destination token - ([04d8b64](https://github.com/IvanLarinDev/dropwheel/commit/04d8b64254806eceef4405ae79bb1b64d70bee22)) - Ivan Larin
#### Documentation
- document new sorter conditions and the size token in the README and demo - ([f016307](https://github.com/IvanLarinDev/dropwheel/commit/f01630727302aa8438237e4d224e047d92c33073)) - Ivan Larin
#### Miscellaneous Chores
- (**design**) add a waiver for token chips in the editor - ([d7f36a6](https://github.com/IvanLarinDev/dropwheel/commit/d7f36a68b091f1cb2e954c870bc5d10975254d40)) - Ivan Larin
- (**design**) add a waiver for media-type condition UI changes - ([4260c37](https://github.com/IvanLarinDev/dropwheel/commit/4260c37f5ff3ba0279359b37bf86c77d60842f2e)) - Ivan Larin
- (**design**) add a waiver for comparison-operator symbols - ([ab5bc25](https://github.com/IvanLarinDev/dropwheel/commit/ab5bc253ae36d830e63b0bfcdc2d3366ef779d91)) - Ivan Larin
- (**design**) add a waiver for creation-date condition UI changes - ([179e9bf](https://github.com/IvanLarinDev/dropwheel/commit/179e9bf43b725065d0b612001f831770120601a3)) - Ivan Larin
- (**design**) add a waiver for rule-duplication UI changes - ([23a95b9](https://github.com/IvanLarinDev/dropwheel/commit/23a95b955c70bf03b61a08c9909e4ec1f2bb7f41)) - Ivan Larin
- (**design**) add a waiver for rule enable/disable UI changes - ([08d97de](https://github.com/IvanLarinDev/dropwheel/commit/08d97dea736478edaa35422a04a71d1843bca398)) - Ivan Larin
- (**design**) add a waiver for NOT-checkbox UI changes - ([26f4e89](https://github.com/IvanLarinDev/dropwheel/commit/26f4e89625961f55f539f6d068522dbe1ccb7ab6)) - Ivan Larin
- (**design**) add a waiver for size-bucket UI changes - ([70cf738](https://github.com/IvanLarinDev/dropwheel/commit/70cf7389e3407f6c0ce4c7c1fd429c2c14d35a41)) - Ivan Larin

- - -
## [v0.17.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.16.4..v0.17.0) - 2026-07-13
#### Features
- (**sort**) add date tokens and folder sorting to rules - ([3c76761](https://github.com/IvanLarinDev/dropwheel/commit/3c767610a3d3b2bf16d906844dfc2550debfd24f)) - Ivan Larin
#### Documentation
- document destination tokens and folder sorting in the README and demo - ([f098453](https://github.com/IvanLarinDev/dropwheel/commit/f0984539331c837b676eb726f850ce3321aec05d)) - Ivan Larin
#### Miscellaneous Chores
- (**design**) add a waiver for token and folder-sorting UI changes - ([0a933b1](https://github.com/IvanLarinDev/dropwheel/commit/0a933b1e9cb197b741fc5812bc6ce1736a30232b)) - Ivan Larin

- - -
## [v0.16.4](https://github.com/IvanLarinDev/dropwheel/compare/v0.16.3..v0.16.4) - 2026-07-13
#### Features
- (**brand**) add an orb-style application icon (variant A) - ([fa69d1f](https://github.com/IvanLarinDev/dropwheel/commit/fa69d1f5cf5e2884dfc551942b78b9eec0701066)) - Ivan Larin
- (**demo**) add confidence-layer and keyboard-navigation scenes - ([c921322](https://github.com/IvanLarinDev/dropwheel/commit/c92132203497cf8fad44a633ac0c3e817309e076)) - Ivan Larin
- (**demo**) show all four Settings sections in the gallery - ([31f287e](https://github.com/IvanLarinDev/dropwheel/commit/31f287e4d4c7260dae938c72b02a80707d9bae9b)) - Ivan Larin
- (**demo**) add a level-overflow scene with a second ring - ([20f2a43](https://github.com/IvanLarinDev/dropwheel/commit/20f2a432ff531c8f40d113b157a910c9039527ac)) - Ivan Larin
- (**demo**) add an interface-window gallery across all four themes - ([a3cb1db](https://github.com/IvanLarinDev/dropwheel/commit/a3cb1db78ba8a79f8bcec1ca21c047b47094c2c5)) - Ivan Larin
#### Documentation
- update the README and demo for 0.16.x - ([dcb1d3d](https://github.com/IvanLarinDev/dropwheel/commit/dcb1d3d6e0eee253f0c478b59755b3b44e26fcb0)) - Ivan Larin

- - -
## [v0.16.3](https://github.com/IvanLarinDev/dropwheel/compare/v0.16.2..v0.16.3) - 2026-07-13
#### Features
- (**history**) record dragged target adds - ([2a1e042](https://github.com/IvanLarinDev/dropwheel/commit/2a1e042a1dc9cc566e339074a91a31c10e2b8618)) - Ivan Larin
- (**history**) link added explorer targets - ([39c04e5](https://github.com/IvanLarinDev/dropwheel/commit/39c04e5d628d507501c1170fb96b1b94402934a6)) - Ivan Larin
- (**history**) reveal created files from tray - ([222ca70](https://github.com/IvanLarinDev/dropwheel/commit/222ca70c0c6b552c2a81133ffe6b23ffa43b411e)) - Ivan Larin
- (**history**) add tray item tooltips - ([bd6703f](https://github.com/IvanLarinDev/dropwheel/commit/bd6703fcac169e854c70202a9e1525d329315377)) - Ivan Larin
- (**history**) clear recent drops from tray - ([98e3d3b](https://github.com/IvanLarinDev/dropwheel/commit/98e3d3bb167f5a59a2a8b74c6ba990adba810a62)) - Ivan Larin
- (**history**) open drop destinations from tray - ([7d0777c](https://github.com/IvanLarinDev/dropwheel/commit/7d0777c1d8eb59aa86cd1b2bdc9e023dc016b3c4)) - Ivan Larin
- (**history**) show recent drops in tray - ([a4de621](https://github.com/IvanLarinDev/dropwheel/commit/a4de621beacee3620a15799b66e4e94dc1f07c8e)) - Ivan Larin
- (**explorer**) add sendto bridge - ([c817484](https://github.com/IvanLarinDev/dropwheel/commit/c817484f52caf3fa3ffde532df325a1802b7ae4f)) - Ivan Larin
- (**drop**) record recent drop history - ([2a1a877](https://github.com/IvanLarinDev/dropwheel/commit/2a1a877b055379cdb4d0153b3696d005cd31c51a)) - Ivan Larin
- (**drop**) add intent filter trust gate - ([07364d0](https://github.com/IvanLarinDev/dropwheel/commit/07364d0c66774d66da85162ba4d2c743618270cb)) - Ivan Larin
#### Bug Fixes
- (**explorer**) add target folders from sendto - ([929f051](https://github.com/IvanLarinDev/dropwheel/commit/929f051b7256a57e419db59deec1a9cd4051a721)) - Ivan Larin
- (**explorer**) keep orb position on sendto - ([e9d480b](https://github.com/IvanLarinDev/dropwheel/commit/e9d480b930e3f7dbde363a52e3d0934e830345e2)) - Ivan Larin
- (**explorer**) polish sendto handoff - ([6f8b7f9](https://github.com/IvanLarinDev/dropwheel/commit/6f8b7f99d230ff964163622d1f5b19d8c7ddf979)) - Ivan Larin
#### Documentation
- (**readme**) document sendto and recent drops - ([111c77b](https://github.com/IvanLarinDev/dropwheel/commit/111c77b5e232330e5367dbe160b0fab85bda3e8d)) - Ivan Larin
#### Miscellaneous Chores
- (**gitignore**) ignore local agent dirs - ([638ac23](https://github.com/IvanLarinDev/dropwheel/commit/638ac2343d4759edf832b7da0b98e74cf8fbaf4f)) - Ivan Larin

- - -
## [v0.16.2](https://github.com/IvanLarinDev/dropwheel/compare/v0.16.1..v0.16.2) - 2026-07-12
#### Features
- (**ui**) clarify target confidence states - ([489d0ef](https://github.com/IvanLarinDev/dropwheel/commit/489d0ef8a150095e5951c4a99a3dfe57309eeb48)) - Ivan Larin

- - -
## [v0.16.1](https://github.com/IvanLarinDev/dropwheel/compare/v0.16.0..v0.16.1) - 2026-07-12
#### Features
- (**ui**) add confidence layer for wheel actions - ([bb951c6](https://github.com/IvanLarinDev/dropwheel/commit/bb951c64fbf3d11863a65eb38fab0244c93a815f)) - Ivan Larin

- - -
## [v0.16.0](https://github.com/IvanLarinDev/dropwheel/compare/v0.15.2..v0.16.0) - 2026-07-12
#### Features
- (**settings**) add advanced hotkey editor - ([1c1def1](https://github.com/IvanLarinDev/dropwheel/commit/1c1def1938db98dedd7e0fc72e70cd3426614431)) - Ivan Larin
#### Bug Fixes
- (**release**) handle delayed workflow discovery - ([8dc4675](https://github.com/IvanLarinDev/dropwheel/commit/8dc4675f6b0b589f99f82d549918f315337a8211)) - Ivan Larin

- - -
## [v0.15.2](https://github.com/IvanLarinDev/dropwheel/compare/v0.15.1..v0.15.2) - 2026-07-12
#### Continuous Integration
- add reliable release automation - ([4eb86ab](https://github.com/IvanLarinDev/dropwheel/commit/4eb86abb6b9102175d62368b9a8947648ea7f628)) - Ivan Larin

- - -
## [v0.15.1](https://github.com/IvanLarinDev/dropwheel/compare/v0.15.0..v0.15.1) - 2026-07-12
#### Bug Fixes
- log failed virtual-file saves and unify the destination-collision check - ([8e07d9f](https://github.com/IvanLarinDev/dropwheel/commit/8e07d9f56297b3912fbca2afd50d6442095d88b8)) - Ivan Larin
#### Refactoring
- MouseHook rewritten as IDisposable after the KeyboardHook pattern, with tests - ([0bb4317](https://github.com/IvanLarinDev/dropwheel/commit/0bb431728dad244f887800069b4faf71dc0618fa)) - Ivan Larin
#### Documentation
- root LLM.md; refreshed README and folder notes - ([3c83a02](https://github.com/IvanLarinDev/dropwheel/commit/3c83a02a90a45bc830aa83ac2a4541140661da82)) - Ivan Larin
- translated the 0.13.0 changelog section into English - ([8276fc0](https://github.com/IvanLarinDev/dropwheel/commit/8276fc09ab4ac233bb3374322632b9fb5155720e)) - Ivan Larin
#### Continuous Integration
- restored the release workflow from the stash and added a CI test run - ([098bcd0](https://github.com/IvanLarinDev/dropwheel/commit/098bcd064576512702d2ff116cc7d1dcd0de0eec)) - Ivan Larin
#### Miscellaneous Chores
- enabled .NET analyzers and added .editorconfig - ([5a40395](https://github.com/IvanLarinDev/dropwheel/commit/5a40395a0b041e47421c331314ae283c3c3b71fb)) - Ivan Larin

- - -
## [v0.13.0](https://github.com/IvanLarinDev/dropwheel/compare/0abeb83284e46c7e8b6993ace622d0b92123279e..v0.13.0) - 2026-07-10
#### Features
- (**groups**) add sequential numeric shortcuts - ([f1e8885](https://github.com/IvanLarinDev/dropwheel/commit/f1e8885801dfae9fbef08be001c1ff40dbaa179c)) - Ivan Larin
- (**orb**) live ghost aura — radar rings over the target - ([cdba305](https://github.com/IvanLarinDev/dropwheel/commit/cdba305c0ef45376649f2987bda50e11ffb99cb9)) - Ivan Larin
- (**orb**) more visible ghost arming over the target; diagnostics removed - ([673b121](https://github.com/IvanLarinDev/dropwheel/commit/673b1214b27a9e998da606c5f3a6853eef521236)) - Ivan Larin
- (**orb**) proximity reaction animations — orb breathing and target highlight - ([1ed9f96](https://github.com/IvanLarinDev/dropwheel/commit/1ed9f967d63d4471953f351a96eb91860bb6f37f)) - Ivan Larin
- (**orb**) pin a target by dragging the orb with Alt+Shift - ([681843d](https://github.com/IvanLarinDev/dropwheel/commit/681843d1319f19aa4ad1480bc6f4d3de96e860f0)) - Ivan Larin
- (**targets**) undo for adding and pinning a target - ([fb13c0d](https://github.com/IvanLarinDev/dropwheel/commit/fb13c0dbf213a393413e5fa393201b88dd4ec39c)) - Ivan Larin
#### Bug Fixes
- (**automation**) use one agents checkout - ([a326902](https://github.com/IvanLarinDev/dropwheel/commit/a326902517c4f163d3a150f84f6d00bd5c2044b4)) - Ivan Larin
- (**groups**) keep shortcuts active in open wheel - ([04de018](https://github.com/IvanLarinDev/dropwheel/commit/04de01833b7388ed7d6bf675d7f878ec8e08be68)) - Ivan Larin
- (**orb**) ghost glow was clipped by the window — the window got margin for the halo - ([5dc45cb](https://github.com/IvanLarinDev/dropwheel/commit/5dc45cb6c89206892098926866fe2c23ae6699c0)) - Ivan Larin
- (**orb**) no more freeze during capture — UIA hit-testing moved to a background thread - ([12a0d5b](https://github.com/IvanLarinDev/dropwheel/commit/12a0d5bd79eb7300eae1e5ee359d5a7b00ef276f)) - Ivan Larin
#### Documentation
- (**demo**) English page version; shared scenes.js and styles.css - ([76eeb1d](https://github.com/IvanLarinDev/dropwheel/commit/76eeb1d50952105ed3e724be42c8062884688845)) - Ivan Larin
- (**demo**) "Interface names" section — the wheel with numbered parts - ([a884138](https://github.com/IvanLarinDev/dropwheel/commit/a884138029cd9a820ea53f77c6bb969570d4e525)) - Ivan Larin
- (**demo**) live ghost aura — radar rings during capture - ([c2f526b](https://github.com/IvanLarinDev/dropwheel/commit/c2f526bf312d5b54fd141ccdb6ff99a28ec6482d)) - Ivan Larin
- (**demo**) removed a redundant subtitle in the header - ([ebf9da8](https://github.com/IvanLarinDev/dropwheel/commit/ebf9da8ddfd4f6c802b61f6075a3c48fff7487e5)) - Ivan Larin
- (**demo**) updated README and LLM for the full scene set - ([5bf9f1f](https://github.com/IvanLarinDev/dropwheel/commit/5bf9f1f65495b6a13407b33e7de392d367599c9e)) - Ivan Larin
- (**demo**) intro text, other-features block and footer - ([c575c19](https://github.com/IvanLarinDev/dropwheel/commit/c575c19813aa735755e9bcd17b68791a491709b9)) - Ivan Larin
- (**demo**) scenes for link targets with favicon, undo and orb behaviour - ([0600fa6](https://github.com/IvanLarinDev/dropwheel/commit/0600fa6bdbce1d1526f3fda99a0a66a4460facff)) - Ivan Larin
- (**demo**) scenes for group codes and tile reordering - ([29e223b](https://github.com/IvanLarinDev/dropwheel/commit/29e223b15468e3a8f4afd5465a1d891df129b551)) - Ivan Larin
- (**demo**) scenes for run targets, sorter and Alt+Shift target capture - ([c6318b4](https://github.com/IvanLarinDev/dropwheel/commit/c6318b432dad39728805a9bba8422a25ea9cb6fb)) - Ivan Larin
- (**demo**) 4 themes, 4 open animations and an interactive main view - ([0e653eb](https://github.com/IvanLarinDev/dropwheel/commit/0e653eb624ddf5562134b4cf225e21a58abbf65a)) - Ivan Larin
- (**demo**) scenes for proximity, entering a group and saving text - ([ed68972](https://github.com/IvanLarinDev/dropwheel/commit/ed68972e6a0b9ae637ca7357aaaa2f019a674f0d)) - Ivan Larin
- (**demo**) orb proximity scene - ([723173c](https://github.com/IvanLarinDev/dropwheel/commit/723173cc19f990c1954413d8b7f470f196010182)) - Ivan Larin
- (**demo**) show orb breathing on proximity in the demo - ([c17e5bc](https://github.com/IvanLarinDev/dropwheel/commit/c17e5bc814186965789bbe6858438c39cd142f33)) - Ivan Larin
- (**demo**) file drag scene with badges and a toast - ([740557f](https://github.com/IvanLarinDev/dropwheel/commit/740557f0c42654f252cf77b9e9bed4bcec21d1f4)) - Ivan Larin
- (**demo**) main wheel view in JS instead of a gif - ([59a25b4](https://github.com/IvanLarinDev/dropwheel/commit/59a25b4cf8f5e7094d286f519a33375be8272a3d)) - Ivan Larin
- (**mockups**) 4 variants of the live ghost aura over the target - ([c39b84d](https://github.com/IvanLarinDev/dropwheel/commit/c39b84d9b80ccc6bf967ef26fb2214eb7364b81d)) - Ivan Larin
- replace gif demos with a link to the live JS demo - ([0b33cc1](https://github.com/IvanLarinDev/dropwheel/commit/0b33cc1f8803abc0970a6615d53c09a7ac075f01)) - Ivan Larin
#### Miscellaneous Chores
- (**design**) waiver for the proximity animation language (breathing variant) - ([68dfcdf](https://github.com/IvanLarinDev/dropwheel/commit/68dfcdf33f49f92201115055df18fa62b5d7aa02)) - Ivan Larin
- (**design**) mockups for the proximity animation language (object→orb, ghost→object) - ([2b1c03e](https://github.com/IvanLarinDev/dropwheel/commit/2b1c03ef6a8009a7d5cff63f7396befb850fdb82)) - Ivan Larin
- (**design**) waiver for group hotkey UI changes - ([82903cd](https://github.com/IvanLarinDev/dropwheel/commit/82903cd6778c6c68062fc084528b03dbf71335f7)) - Ivan Larin
- (**harness**) harness and release hook updates - ([b272e37](https://github.com/IvanLarinDev/dropwheel/commit/b272e37c8de2ba27931ab30ada8cc8adc1e8f506)) - Ivan Larin
- (**harness**) restore pipeline automation - ([0abeb83](https://github.com/IvanLarinDev/dropwheel/commit/0abeb83284e46c7e8b6993ace622d0b92123279e)) - Ivan Larin
- (**orb**) diagnostics for entering the capture gesture (mousedown, begin) - ([7b1863d](https://github.com/IvanLarinDev/dropwheel/commit/7b1863dfa8ef9818775c9669311219e4ddbe81c3)) - Ivan Larin
- (**orb**) temporary diagnostics for target recognition during capture - ([fcf6669](https://github.com/IvanLarinDev/dropwheel/commit/fcf6669d8da0d9df42f9b780ecaa40da1a39b3b4)) - Ivan Larin
- ignore automation output inbox/ and reports/ - ([6f08385](https://github.com/IvanLarinDev/dropwheel/commit/6f0838575fdcae22c24460549617d6ca490173d4)) - Ivan Larin

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
