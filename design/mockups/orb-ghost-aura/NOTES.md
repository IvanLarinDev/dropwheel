# Mockups - orb-ghost-aura

DESIGN classification: `animation` / `js`.
Concrete example: during an Alt+Shift capture, the ghost hovers a valid target and its aura is
alive rather than a static glow. Each variant shows the ghost flying in from the left, arming as
it reaches the target (the frame around the file is the working target highlight), then looping
its aura character while it holds over the target.

## Why

The ghost → object half of the proximity language now shows the ghost's arm level as a plain
glow that fades in and then sits still. A living aura reads as "the ghost has locked on" and
keeps the moment feeling active while the user decides to release.

## Variants

- `01-breathing-pulse.html` - the halo swells and fades in a slow rhythm; ties back to the orb's breath.
- `02-radar-ping.html` - rings expand outward in a steady beat, a sensor locking on.
- `03-orbiting-sparks.html` - accent dots circle the ghost, faster as it arms.
- `04-sweeping-arc.html` - a bright arc sweeps around the aura's rim, a rotating scan.

## Review rule

Each HTML file must remain an executable JavaScript motion prototype for the same concrete
scenario and visual baseline. The four variants differ only in the aura's character.

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Add the chosen character to the existing `APPROVED` (or note it) for `src/Dropwheel/UI/**`.
4. Implement the user-visible change only after approval.
