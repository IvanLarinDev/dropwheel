using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private double _chargeTarget, _charge;   // 0..1 nearness of an incoming drag
    private double _lookX, _lookY, _lookXs, _lookYs; // unit direction to the cursor, raw and smoothed
    private double _breathPhase;
    private bool _breathing, _beatHolding;
    private DispatcherTimer? _beat;

    /// <summary>Half of the wheel-open threshold and the outer edge of the "anticipation" zone,
    /// both in device pixels, filled by <see cref="UpdateOrbScreenPos"/>.</summary>
    private double _openR, _outerR;

    private const double LookReach = 4;      // how far the core drifts toward the cursor, DIP
    private const double HaloPeak = 0.5;     // halo opacity at full charge
    private const double BreathIdle = 0.04;  // core pulse amplitude with no charge
    private const double BreathGain = 0.16;  // extra amplitude added at full charge

    /// <summary>Reacts to an incoming drag: while the left button is held and the cursor nears the
    /// orb, the orb charges up (halo, breathing core, a lean toward the cursor). Crossing the open
    /// threshold holds one short beat, then opens the wheel — the inhale flowing into the reveal.
    /// Alt is reserved for the move/capture gestures, so it never charges.</summary>
    private void UpdateCharge(int x, int y, double d2, bool leftDown)
    {
        if (!leftDown || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _chargeTarget = 0;
            CancelBeat();
            EnsureBreathing();
            return;
        }

        double dist = Math.Sqrt(d2);
        _chargeTarget = ChargeFor(dist, _openR, _outerR);
        if (dist > 1) { _lookX = (x - _orbSX) / dist; _lookY = (y - _orbSY) / dist; }
        EnsureBreathing();

        if (d2 > _closeR2) CancelBeat();
        if (!_open && d2 < _openR2 && _beat == null) StartBeat();
    }

    /// <summary>Nearness charge 0..1: zero at or beyond the outer edge of the anticipation zone,
    /// one at or inside the open threshold, linear between. All distances in the same units.</summary>
    internal static double ChargeFor(double dist, double openR, double outerR)
        => Math.Clamp((outerR - dist) / (outerR - openR), 0, 1);

    /// <summary>The held beat at the threshold: the orb holds its inhale, then opens the wheel.
    /// Cancelled if the drag pulls away or releases before it fires.</summary>
    private void StartBeat()
    {
        _beatHolding = true;
        _beat = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(ScaleTiming(170, AnimationSpeed())) };
        _beat.Tick += (_, _) =>
        {
            CancelBeat();
            if (!_open) { _proximityOpened = true; OpenCloud(); }
        };
        _beat.Start();
    }

    private void CancelBeat()
    {
        _beat?.Stop();
        _beat = null;
        _beatHolding = false;
    }

    private void EnsureBreathing()
    {
        if (_breathing) return;
        _breathing = true;
        CompositionTarget.Rendering += OnBreathe;
    }

    private void StopBreathing()
    {
        if (!_breathing) return;
        _breathing = false;
        CompositionTarget.Rendering -= OnBreathe;
        Halo.Opacity = 0;
        HaloScale.ScaleX = HaloScale.ScaleY = 1;
        HubCoreScale.ScaleX = HubCoreScale.ScaleY = 1;
        HubCoreLook.X = HubCoreLook.Y = 0;
    }

    /// <summary>Per-frame breath: eases the charge toward its target and drives the halo glow, the
    /// core's slow pulse and its lean toward the cursor. Unhooks itself once fully faded.</summary>
    private void OnBreathe(object? sender, EventArgs e)
    {
        _charge += (_chargeTarget - _charge) * 0.18;
        if (_charge < 0.002 && _chargeTarget == 0) { StopBreathing(); return; }

        double ce = _charge * _charge * (3 - 2 * _charge); // smoothstep
        Halo.Opacity = ce * HaloPeak;
        HaloScale.ScaleX = HaloScale.ScaleY = 1 + ce * 0.5;

        _lookXs += (_lookX - _lookXs) * 0.2;
        _lookYs += (_lookY - _lookYs) * 0.2;
        HubCoreLook.X = _lookXs * LookReach * ce;
        HubCoreLook.Y = _lookYs * LookReach * ce;

        double amp = BreathIdle + BreathGain * ce;
        double scale = _beatHolding ? 1 + BreathIdle + BreathGain : 1 + amp * Math.Sin(_breathPhase);
        HubCoreScale.ScaleX = HubCoreScale.ScaleY = scale;
        _breathPhase += 0.10;
    }
}
