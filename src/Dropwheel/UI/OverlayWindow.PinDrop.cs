using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dropwheel.Models;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>How far the arc bows away from the hub, in pixels at its midpoint.</summary>
    private const double PinArcLift = 46;

    private bool _pinRingVisible;

    /// <summary>Shows or hides the accent ring around the orb during an Alt+Shift capture drag: the
    /// ring lights up while a valid target sits under the cursor, promising that releasing now pins
    /// it. Guarded by a flag because the move handler fires continuously and would otherwise restart
    /// the animation on every tick.</summary>
    private void SetPinRing(bool visible)
    {
        if (visible == _pinRingVisible) return;
        _pinRingVisible = visible;

        var duration = TimeSpan.FromMilliseconds(ScaleTiming(visible ? 140 : 110, AnimationSpeed()));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AnimatePinRing(visible ? 1 : 0, visible ? 1.42 : 1.0, duration, ease);
    }

    /// <summary>A single outward pulse for a pin that happened while the wheel was closed and no
    /// tile could fly anywhere. Leaves the ring ready for the next drag.</summary>
    private void PulsePinRing()
    {
        _pinRingVisible = false;
        var duration = TimeSpan.FromMilliseconds(ScaleTiming(280, AnimationSpeed()));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(1, 0, duration);
        fade.Completed += (_, _) => ResetPinRing();
        PinRing.BeginAnimation(OpacityProperty, fade);
        PinRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 1.9, duration) { EasingFunction = ease });
        PinRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 1.9, duration) { EasingFunction = ease });
    }

    private void AnimatePinRing(double opacity, double scale, TimeSpan duration, IEasingFunction ease)
    {
        PinRing.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
        PinRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        PinRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
    }

    private void ResetPinRing()
    {
        PinRing.BeginAnimation(OpacityProperty, null);
        PinRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PinRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PinRing.Opacity = 0;
        PinRingScale.ScaleX = PinRingScale.ScaleY = 1;
    }

    /// <summary>Guided arc: each pinned tile is drawn from the orb centre to its new slot along a
    /// curve bowed away from the hub, so the captured object and the tile it became stay visually
    /// linked. Falls back to a hub pulse when the wheel is closed.</summary>
    private void AnimatePinnedArrival(IReadOnlyList<TargetItem> pinned, Point origin)
    {
        if (!_open) { PulsePinRing(); return; }

        var elements = Cloud.Children
            .OfType<FrameworkElement>()
            .Where(el => el.Tag is TargetItem)
            .GroupBy(el => (TargetItem)el.Tag!)
            .ToDictionary(group => group.Key, group => group.First());

        var speed = AnimationSpeed();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < pinned.Count; i++)
            if (elements.TryGetValue(pinned[i], out var element))
                AnimateArcToSlot(element, origin, ScaleTiming(300, speed), ScaleTiming(i * 40, speed), ease);
    }

    private static void AnimateArcToSlot(
        FrameworkElement element, Point origin, int durationMs, int delayMs, IEasingFunction ease)
    {
        double toLeft = Canvas.GetLeft(element), toTop = Canvas.GetTop(element);
        if (double.IsNaN(toLeft) || double.IsNaN(toTop)) return;

        double fromLeft = origin.X - TileLeftOffset, fromTop = origin.Y - TileTopOffset;
        var (midLeft, midTop) = BowedMidpoint(fromLeft, fromTop, toLeft, toTop);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var delay = TimeSpan.FromMilliseconds(delayMs);

        ClearTileTransform(element);
        element.BeginAnimation(Canvas.LeftProperty, null);
        element.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(element, fromLeft);
        Canvas.SetTop(element, fromTop);
        Panel.SetZIndex(element, 10);

        var left = ArcAnimation(midLeft, toLeft, duration, ease);
        var top = ArcAnimation(midTop, toTop, duration, ease);
        left.BeginTime = delay;
        top.BeginTime = delay;
        top.Completed += (_, _) =>
        {
            element.BeginAnimation(Canvas.LeftProperty, null);
            element.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(element, toLeft);
            Canvas.SetTop(element, toTop);
            Panel.SetZIndex(element, 0);
        };
        element.BeginAnimation(Canvas.LeftProperty, left);
        element.BeginAnimation(Canvas.TopProperty, top);

        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(40, durationMs / 2))) { BeginTime = delay });

        if (TileScale(element) is { } scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.55, 1, duration) { BeginTime = delay, EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.55, 1, duration) { BeginTime = delay, EasingFunction = ease });
        }
    }

    /// <summary>Drops the opening animation's transform so the arc starts from a clean tile.</summary>
    private static void ClearTileTransform(FrameworkElement element)
    {
        if (element.RenderTransform is not TransformGroup group) return;
        foreach (var translate in group.Children.OfType<TranslateTransform>())
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.X = translate.Y = 0;
        }
    }

    /// <summary>Midpoint of the flight, pushed outward along the ray from the hub. A drop on the hub
    /// itself has no direction of its own, so the destination's ray is used instead.</summary>
    internal static (double Left, double Top) BowedMidpoint(
        double fromLeft, double fromTop, double toLeft, double toTop)
    {
        double midX = (fromLeft + toLeft) / 2 + TileLeftOffset;
        double midY = (fromTop + toTop) / 2 + TileTopOffset;
        double dx = midX - HalfSize, dy = midY - HalfSize;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            dx = toLeft + TileLeftOffset - HalfSize;
            dy = toTop + TileTopOffset - HalfSize;
            length = Math.Sqrt(dx * dx + dy * dy);
        }
        if (length < 1) return (midX - TileLeftOffset, midY - TileTopOffset);

        return (midX + dx / length * PinArcLift - TileLeftOffset,
                midY + dy / length * PinArcLift - TileTopOffset);
    }
}
