using System.Windows;
using System.Windows.Input;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private readonly MouseHook _mouseHook = new();
    private bool _proximityOpened, _movingOrb;
    private bool _prevLeftDown, _suppressProximity;
    private double _orbSX, _orbSY, _openR2, _closeR2;

    private void InitProximity()
    {
        UpdateOrbScreenPos();
        _mouseHook.MouseMoved += OnGlobalMouse;
        Closed += (_, _) => { _mouseHook.Dispose(); StopBreathing(); };
        _mouseHook.Start(); // hook is installed from the UI thread → callback runs there too
    }

    private void UpdateOrbScreenPos()
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } ct }) return;
        var p = Orb.PointToScreen(new Point(23, 23)); // orb center, device px
        _orbSX = p.X; _orbSY = p.Y;
        double m = ct.TransformToDevice.M11;
        _openR = 150 * m;
        _outerR = 300 * m;             // outer edge of the anticipation zone
        _openR2 = _openR * _openR;     // open radius
        _closeR2 = 340 * m * 340 * m;  // close radius
    }

    /// <summary>Held LMB with the cursor near the orb means a likely drag →
    /// open the wheel early, before the drag enters our window.</summary>
    private void OnGlobalMouse(int x, int y, bool leftDown)
    {
        if (_movingOrb) return;
        if (!IsVisible) return; // hidden by the fullscreen detector — don't charge or open on a hidden window
        double dx = x - _orbSX, dy = y - _orbSY, d2 = dx * dx + dy * dy;
        WakeIdle(d2);

        // The exact suppression/open/close transition lives in a pure, unit-tested state machine, so the
        // recurring "wheel opens by itself" regressions stay locked down. This frame only reads the live
        // flags into it and applies the single action it returns.
        var (next, intent) = new ProximityState(_prevLeftDown, _suppressProximity, _proximityOpened)
            .Step(leftDown, d2, _openR2, _closeR2, _open);
        _prevLeftDown = next.PrevLeftDown;
        _suppressProximity = next.Suppressed;
        _proximityOpened = next.ProximityOpened;

        UpdateCharge(x, y, d2, leftDown, intent); // charges the orb and, on StartBeat, holds the beat
        if (intent == ProximityIntent.Close) CloseCloud();
    }
}
