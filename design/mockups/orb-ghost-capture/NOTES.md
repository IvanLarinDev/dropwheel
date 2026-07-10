# Mockups - orb-ghost-capture

DESIGN classification: `animation` / `js`.
Concrete example: Alt+Shift drags a ghost orb onto a file; the ghost flies back into the orb, the wheel opens, and the captured tile arrives first.

## Review rule

Each HTML file must remain an executable JavaScript motion prototype for the same concrete scenario and visual baseline.

The main orb stays put; a light ghost follows the cursor during the Alt+Shift drag.
On release over a valid target the ghost returns to the orb, the wheel opens, and the
captured tile arrives first. The four variants differ in the character of that chain.

## Variants

- `01-direct-ease.html` - Snap back: ghost snaps in, wheel opens at once, tile flies straight.
- `02-soft-settle.html` - Soft settle: eased return, blooming wheel, small overshoot on the tile.
- `03-staggered-reflow.html` - Two-step confirm: ghost returns, orb pulses a confirming ring, a beat, then the wheel opens.
- `04-guided-arc.html` - Trailed arc: ghost curves back with a fading trail, tile flies out along the same arc.

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an `APPROVED` file in this directory with a `ui: <changed-ui-path-or-glob>` line.
4. Implement the user-visible change only after approval.
