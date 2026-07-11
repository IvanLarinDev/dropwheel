# Mockups - add-while-open

DESIGN classification: `animation` / `js`.
Concrete example: Dropping a file onto an already-open wheel adds one tile; only that new tile animates in while the existing tiles stay put.

Problem: dropping a file onto an already-open wheel replays the full opening animation for every
tile, not just the new one. These four motions each animate only the new tile; existing tiles hold
or slide rather than re-appearing. All drawn in the app's dark palette.

## Variants (APPROVED: 04-guided-arc)

- `01-direct-ease.html` - New tile eases in at its slot, no overshoot (160ms).
- `02-soft-settle.html` - New tile arrives with a small overshoot and settle (240ms).
- `03-staggered-reflow.html` - Neighbours shift to open a gap, then the new tile drops in.
- `04-guided-arc.html` - New tile flies from the hub on an arc to its slot; matches pin-arrival. **Chosen.**

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an `APPROVED` file in this directory with a `ui: <changed-ui-path-or-glob>` line.
4. Implement the user-visible change only after approval.
