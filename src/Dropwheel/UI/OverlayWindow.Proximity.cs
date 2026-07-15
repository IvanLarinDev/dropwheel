using System.Windows;
using System.Windows.Input;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private readonly MouseHook _mouseHook = new();
    private bool _proximityOpened, _movingOrb;
    private bool _prevLeftDown, _suppressProximity;
    private double _orbSX, _orbSY, _closeR, _openR2, _closeR2;

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
        var p = Orb.PointToScreen(new Point(Orb.Width / 2, Orb.Height / 2)); // orb center, device px
        _orbSX = p.X; _orbSY = p.Y;
        double m = ct.TransformToDevice.M11;
        double s = WheelLayout.ClampScale(TargetStore.Config.WheelScale); // zones track the scaled wheel
        _openR = 150 * m * s;
        _outerR = 300 * m * s;   // outer edge of the anticipation zone
        _closeR = 340 * m * s;   // the wheel stays open until the cursor leaves this radius
        _openR2 = _openR * _openR;
        _closeR2 = _closeR * _closeR;
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
