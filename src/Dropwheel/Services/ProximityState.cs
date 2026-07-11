namespace Dropwheel.Services;

/// <summary>What the proximity transition asks the UI to do this frame. Rendering (halo, breathing)
/// is not represented here — only the discrete open/close actions.</summary>
public enum ProximityIntent { None, StartBeat, Close }

/// <summary>The pure proximity/suppression state machine behind the "wheel opens as a drag nears the
/// orb" behavior. Kept free of WPF so the exact transitions that repeatedly caused "the wheel opens by
/// itself" bugs are locked down by unit tests. The instance flags mirror the OverlayWindow fields:
/// whether the left button was down last frame, whether proximity-open is suppressed (a press that
/// began on/inside the zone), and whether the wheel was opened by proximity (so it may proximity-close).
/// </summary>
public readonly record struct ProximityState(bool PrevLeftDown, bool Suppressed, bool ProximityOpened)
{
    /// <summary>Advances one mouse frame. <paramref name="d2"/> is the squared distance from the orb
    /// center, compared against the squared open/close radii; <paramref name="open"/> is whether the
    /// wheel is currently open. Returns the next state and the single action to take.</summary>
    public (ProximityState Next, ProximityIntent Intent) Step(
        bool leftDown, double d2, double openR2, double closeR2, bool open)
    {
        bool suppressed = Suppressed;
        bool opened = ProximityOpened;

        // A press that begins on/inside the open zone is a click or gesture, not an approaching drag,
        // and must not auto-open — otherwise clicking to close instantly reopens.
        if (leftDown && !PrevLeftDown && d2 < openR2) suppressed = true;
        // Releasing clears both latches: the next approach starts fresh.
        if (!leftDown) { opened = false; suppressed = false; }

        var intent = ProximityIntent.None;
        if (open && opened && d2 > closeR2) { opened = false; intent = ProximityIntent.Close; }
        else if (leftDown && !open && d2 < openR2 && !suppressed) intent = ProximityIntent.StartBeat;

        return (new ProximityState(leftDown, suppressed, opened), intent);
    }
}
