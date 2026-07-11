using System.Windows;
using System.Windows.Input;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private bool _proximityOpened, _movingOrb;
    private bool _prevLeftDown, _suppressProximity;
    private double _orbSX, _orbSY, _openR2, _closeR2;

    private void InitProximity()
    {
        UpdateOrbScreenPos();
        MouseHook.MouseMoved += OnGlobalMouse;
        Closed += (_, _) => { MouseHook.MouseMoved -= OnGlobalMouse; MouseHook.Stop(); StopBreathing(); };
        MouseHook.Start(); // hook is installed from the UI thread → callback runs there too
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
        double dx = x - _orbSX, dy = y - _orbSY, d2 = dx * dx + dy * dy;
        WakeIdle(d2);

        // Proximity is meant for an external drag approaching from outside. A press that begins on or
        // right next to the orb (clicking it, an Alt-drag, just holding the button over it) is not an
        // approaching drag and must not auto-open — otherwise clicking to close instantly reopens.
        if (leftDown && !_prevLeftDown) _suppressProximity = d2 < _openR2;
        _prevLeftDown = leftDown;
        if (!leftDown) { _proximityOpened = false; _suppressProximity = false; }

        UpdateCharge(x, y, d2, leftDown); // charges the orb and, at the threshold, opens the wheel
        if (_open && _proximityOpened && d2 > _closeR2)
        { _proximityOpened = false; CloseCloud("proximity-retreat"); }
    }
}
