# hotkey-chips — chosen variant

Four variants for replacing manual hotkey input with clickable suggestion chips
are in `mockups.html`. The "Common combinations" dropdown is removed in all of them.

**Chosen: Variant 4 — chips + Record only, no manual text input.**

Both hotkey fields become read-only displays. A combination is picked from the
chip row under each field or recorded via the Record button. The second hotkey's
row starts with an Off chip that clears it. A chip held by the other hotkey field
is struck through and not clickable. Typos are impossible by construction, so the
"does it parse" validation only matters for recorded combos.

Approved by: Ivan Larin, 2026-07-14.
