# docs/demo

Live Dropwheel interface documentation for the GitHub Pages site. The target
wheel and every interaction are rendered directly in the browser with
JavaScript and canvas. This replaces the screen-recording GIFs that previously
lived in `docs/media`.

## Contents

- `index.html` — the Russian-language demo page. Each scene demonstrates one
  workflow with a written explanation; the bottom of the page contains the
  "More features" cards.
- `index.en.html` — the English version of the same page. Both pages share the
  engine, scenes, and styles and differ only in their text. An RU / EN switcher
  appears at the top of each page.
- `dropwheel.js` — the rendering engine. `DW.Wheel(canvas, tiles, opts)` draws
  the wheel and plays its animations. Geometry, timing, and colors mirror the
  real application (`src/Dropwheel/UI/OverlayWindow.*`, `UI/Themes.cs`).
- `scenes.js` — shared code for every scene, with timelines driven by
  `requestAnimationFrame`. The only localized string comes from `window.DW_TXT`,
  which each page defines before loading the script.
- `styles.css` — shared page styles.

## Scenes

The demo covers the main view (with animation, speed, and theme selectors), a
numbered interface glossary, file dragging with copy and move actions,
proximity opening, entering a group, saving text, opening files with an app,
sorting into subfolders, pinning a target with the orb (Alt+Shift), group codes
(1 versus 11), reordering tiles around the rim, links with titles and favicons,
undo, and orb movement and fading.

## Viewing the demo

Open `index.html` directly in a browser, or start a static server from the demo
root:

    python -m http.server 8777 --directory docs/demo

Then visit `http://localhost:8777`.

## Adding scenes

A scene's markup is a `<section class="scene">` block containing a canvas and a
description on both pages (`index.html` and `index.en.html`). The scene logic is
shared in `scenes.js`. Each scene is a `requestAnimationFrame` function that
updates the attached `DW.Wheel` state every frame (see the engine fields below)
and renders it. Constructor options are `theme` (Fluent/Dark/Light/Neon),
`animation` (pop/burst/sweep/settle), `speed`, `startOpen`, and `hover`. When
adding a scene, update the markup on both language pages and the logic in
`scenes.js`; read localized scene text from `window.DW_TXT`.

Controllable state on a `Wheel` object includes `forceHot`, `badges` (a map from
index to type), `ghost` (kind file/text/orb), `toast`, `flash`, `chips`,
`highlight`, `pinRing`, `cursor`, `orbPulse`/`orbLook`, `orbOffset`/`orbAlpha`,
`tileAngles`, `tileMul`, and `orbBadge`. Methods include `open()`, `close()`,
`setTheme/setAnimation/setSpeed`, and `tileCenter(i)`.

Targets are supplied as an array of `{ label, icon, group, sorter, num, code,
add, back, fav }` objects. This is demonstration data and does not need to match
the user's real folders.

## Avoid

- Do not add real Windows shell icons. `dropwheel.js` draws vector icons so the
  demo remains self-contained and lightweight.
- Do not approximate application geometry or timing by eye. Change these values
  only when the corresponding code in `src/Dropwheel/UI` changes.
