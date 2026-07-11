# Mockups — group-delete-safety

DESIGN classification: `new-ui`.

Problem: deleting a group from the target editor is instant, silently removes every child
target (folders, apps, sort rules), and cannot be undone. These four concepts protect
against accidental data loss. All are drawn in the app's dark overlay palette.

## Concepts

- `01-minimal-light.html` — **Confirm dialog.** A modal names the group and its child count
  and requires an explicit "Delete group & N items". Simplest, one new dialog.
- `02-dark-pro.html` — **Confirm with a safe alternative.** The dialog offers a
  non-destructive path: keep the targets (move them out to the main wheel) vs. delete
  everything. Prevents loss entirely for users who only wanted to drop the container.
- `03-high-contrast-a11y.html` — **Undo toast, no modal.** Delete stays one click, but a
  prominent timed Undo toast (reusing the existing toast + undo machinery) lets a mistake
  be reversed. No interruption, relies on the user noticing the toast.
- `04-playful-rounded.html` — **Inline two-step.** No modal; the editor's Delete button
  arms itself and needs a second deliberate click ("Confirm — delete group & N items").
  Lightweight, keeps everything in the editor.

## Closing the gate

1. User chooses a direction.
2. Create an `APPROVED` file here with a `ui:` line scoping the changed UI paths.
3. Implement only after approval.
