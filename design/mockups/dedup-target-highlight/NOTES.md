# Mockups - dedup-target-highlight

DESIGN classification: `animation` / `js`.
Concrete example: Dropping a folder already on the wheel highlights its existing tile instead of adding a duplicate.

When the optional "Skip duplicate targets" setting is on and a dropped folder, app or link
already sits on the current wheel level, no new tile is added. Instead a toast says it is
already there and the existing tile is highlighted so the eye can find it. These four variants
are different ways to do that highlight, on the same wheel and the same scenario.

## Review rule

Each HTML file is an executable motion prototype for the same scenario and the same wheel.
Compare the feel of the highlight only; the wheel, tiles and toast are identical across all four.

## Variants

- `01-scale-pulse.html` - the existing tile bounces up in scale and settles (quiet, cheap).
- `02-accent-glow.html` - an accent halo and border swell around the tile and fade (no movement).
- `03-nudge-shake.html` - the tile shakes side to side (loud, risks an error-buzz feel).
- `04-flash-fill.html` - the tile face flushes accent colour and drains back (unmistakable, loudest).

## Closing the gate

1. Review the four highlights and choose a direction.
2. Create an `APPROVED` file in this directory with a `ui: src/Dropwheel/UI/` scope line.
3. Implement the chosen highlight in `PulseExistingTiles` / `PulseTile` only after approval.
