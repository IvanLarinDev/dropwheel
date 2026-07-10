# Mockups - orb-proximity-language

DESIGN classification: `animation` / `js`.
Concrete example: proximity reactions read as one motion phrase. A dragged object nears
the orb (object → orb) and the orb charges up and latches the wheel open; then the orb
sends a ghost out to a target (ghost → object), the target lights, and the ghost furls
back into the orb.

## The shared law: "charge"

The orb is the home of the charge. Charge either arrives from outside (the user drags an
object toward the orb) or the orb sends a ghost out to fetch it (Alt+Shift capture) and
draws it back. So both proximity events are two halves of one arc, and both obey the same
four primitives:

- Charge — one value 0..1 as a function of nearness. It grows the orb's halo and breathing
  as an object approaches, and arms the ghost's core as it nears a target. One law, both sides.
- Look — the near thing leans toward the other: the orb's core drifts toward the object,
  the ghost's core drifts toward the target.
- Latch — crossing the threshold is a single "click": a ring flash plus a short inhale.
  Opening the wheel and confirming a valid target are the same latch, same ring language.
- Furl — on the way out the charge collapses inward into that same confirming ring (this is
  the tail the existing orb-ghost-capture sequence already plays).

`object → orb` and `ghost → object` are mirror images: charge flows in, then out.

## Review rule

Each HTML file must remain an executable JavaScript motion prototype for the same concrete
scenario and visual baseline. The four variants differ only in the character of the phrase.

## Note on the second half (ghost → object)

The target object lives in another app's window (Explorer / desktop) and cannot be animated
by us. In the real app the "object reacts" illusion is our own highlight drawn over the
item via UI Automation bounds, and a per-frame hit-test is expensive, so it must be
throttled. The prototypes stand in a fake target on the right to show the intended feel.

## Closing the gate

1. Refine every variant into credible evidence for this feature.
2. Review the alternatives and choose a direction.
3. Create an `APPROVED` file in this directory with a `ui: <changed-ui-path-or-glob>` line.
4. Implement the user-visible change only after approval.
