using System.Windows.Input;

namespace Dropwheel.Services;

/// <summary>What a left-drag on the orb means, decided by the modifiers held at mouse-down.</summary>
public enum OrbDragKind
{
    /// <summary>Plain drag — no orb action (the click toggles the wheel instead).</summary>
    None,

    /// <summary>Alt-drag relocates the orb and remembers the new position.</summary>
    Move,

    /// <summary>Alt+Shift-drag captures whatever folder, app or file the orb is released over
    /// and pins it as a target.</summary>
    Capture,
}

/// <summary>Reads the orb's left-drag intent from the keyboard. Alt+Shift is checked before Alt
/// alone so the capture gesture wins over plain relocation.</summary>
public static class OrbGesture
{
    public static OrbDragKind Classify(ModifierKeys modifiers)
    {
        bool alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (!alt) return OrbDragKind.None;
        return modifiers.HasFlag(ModifierKeys.Shift) ? OrbDragKind.Capture : OrbDragKind.Move;
    }
}
