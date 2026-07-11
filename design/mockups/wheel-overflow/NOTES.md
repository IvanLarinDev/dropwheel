# Mockups — wheel-overflow

DESIGN classification: `layout` / `js`.

Concrete problem: one wheel level can hold roughly 10–12 tiles on the single rim
(radius 170, tiles 64px) before they crowd. Today the only answer to "too many
targets" is a group. This feature adds a second concentric ring so the surplus
flows outward on the same level, keeping the drop target generous.

The mockups reuse the real visual baseline from `docs/demo/dropwheel.js`: same
theme palettes, hub, spokes, tile style, captions. Only the layout differs. Each
page has a **targets** slider (5–16) and a **theme** switch so you can watch the
second ring appear and compare behaviour across the four themes. The readout
shows the tiles-per-ring split and the implied window size.

## The shared law: the drop target must stay big

Dropwheel is a wheel you drop files onto, so a target tile must stay large enough
to hit while dragging. That rules out an *inner* overflow ring (shorter
circumference → cramped tiles). All four variants therefore push the surplus
*outward*, where a larger circumference gives more room at the same tile size.
They differ in how far the window has to grow, whether the inner wheel stays
familiar, and how the two rings relate angularly.

## What each variant is really testing

- **01 Split balanced** — symmetry and roominess at the cost of the biggest window.
- **02 Overflow band** — least disruption: the inner wheel looks exactly like today
  until you exceed the cap, then a clearly-secondary outer band appears.
- **03 Petals** — density: fit the most tiles in the smallest window, accepting a
  smaller outer-row target.
- **04 Concentric columns** — a structured two-band identity with column-wide
  hover; the two rings read as one grid rather than two separate wheels.

## Open questions to decide with the direction

- Auto (rings/threshold self-adjust to tile count) vs a settings slider. KISS
  argues for auto with an optional cap.
- The window is fixed 460×460 today (`HalfSize = 230`). Any real overflow ring
  needs the window and orb-centering to grow; this touches proximity, spokes,
  drop hit-test and the open animations, which all assume one ring.
- Which ring the "+" tile lives on once overflow is active.

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an `APPROVED` file in this directory with a `ui: <changed-ui-path-or-glob>` line.
4. Implement the user-visible change only after approval.
