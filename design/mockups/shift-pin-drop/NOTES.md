# Mockups - shift-pin-drop

DESIGN classification: `animation` / `js`.
Concrete example: Alt+Shift-drag the orb onto a folder/app/file in Explorer or on the desktop. A pin ring lights while the orb is over a valid target; on release the captured tile lands first on the rim along a guided arc.

## Review rule

Each HTML file must remain an executable JavaScript motion prototype for the same concrete scenario and visual baseline.

## Variants

- `01-direct-ease.html` - Fast, restrained feedback with no overshoot.
- `02-soft-settle.html` - A visible arrival with a small overshoot and settle.
- `03-staggered-reflow.html` - Affected elements move in a short sequence so the reflow is easy to follow.
- `04-guided-arc.html` - A curved path makes the source and destination relationship explicit.

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an `APPROVED` file in this directory with a `ui: <changed-ui-path-or-glob>` line.
4. Implement the user-visible change only after approval.
