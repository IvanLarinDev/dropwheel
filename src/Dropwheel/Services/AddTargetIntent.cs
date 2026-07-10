using System.Windows;

namespace Dropwheel.Services;

/// <summary>Reads the keyboard state carried by a drop on the orb, the hub or the "+" tile.
/// Shift means "pin the new target": it is stored pinned and placed first on its level.
/// Only the drag payload's key states are trusted here; Keyboard.Modifiers is unreliable
/// while an OLE drag owns the input queue.</summary>
public static class AddTargetIntent
{
    public static bool ShouldPin(DragDropKeyStates keys) => keys.HasFlag(DragDropKeyStates.ShiftKey);
}
