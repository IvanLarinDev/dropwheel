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

    /// <summary>A single outward pulse of the orb ring — the "captured" confirmation the ghost
    /// hands off to before the wheel opens.</summary>
    private void PulsePinRing()
    {
        var duration = TimeSpan.FromMilliseconds(ScaleTiming(300, AnimationSpeed()));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation(1, 0, duration);
        fade.Completed += (_, _) => ResetPinRing();
        PinRing.BeginAnimation(OpacityProperty, fade);
        PinRingScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 1.9, duration) { EasingFunction = ease });
        PinRingScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 1.9, duration) { EasingFunction = ease });
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

        var elements = TileElementsByTarget();

        var speed = AnimationSpeed();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < pinned.Count; i++)
            if (elements.TryGetValue(pinned[i], out var element))
                AnimateArcToSlot(element, origin, ScaleTiming(300, speed), ScaleTiming(i * 40, speed), ease);
    }

    private void AnimateArcToSlot(
        FrameworkElement element, Point origin, int durationMs, int delayMs, IEasingFunction ease)
    {
        double toLeft = Canvas.GetLeft(element), toTop = Canvas.GetTop(element);
        if (double.IsNaN(toLeft) || double.IsNaN(toTop)) return;

        double fromLeft = origin.X - TileLeftOffset, fromTop = origin.Y - TileTopOffset;
        var (midLeft, midTop) = BowedMidpoint(fromLeft, fromTop, toLeft, toTop, HalfSize);
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

    /// <summary>Briefly pulses the tiles a duplicate drop collided with, so the eye finds the target
    /// that is already on the wheel. Only the level currently on screen has tiles, so a collision on
    /// another level (or a closed wheel) falls back to the toast alone.</summary>
    private void PulseExistingTiles(IReadOnlyList<TargetItem> targets, TargetItem? group)
    {
        if (!_open || !ReferenceEquals(group, _currentGroup) || targets.Count == 0) return;

        var elements = TileElementsByTarget();

        foreach (var target in targets.Distinct())
            if (elements.TryGetValue(target, out var element))
                PulseTile(element);
    }

    /// <summary>One scale bounce on a tile: it swells and settles back, matching the wheel's existing
    /// tile emphasis. Reuses the tile's own ScaleTransform so nothing else on the rim moves.</summary>
    private void PulseTile(FrameworkElement element)
    {
        if (TileScale(element) is not { } scale) return;
        var duration = TimeSpan.FromMilliseconds(ScaleTiming(360, AnimationSpeed()));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        var pulse = new DoubleAnimation(1.18, 1, duration) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
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
        double fromLeft, double fromTop, double toLeft, double toTop, double center)
    {
        double midX = (fromLeft + toLeft) / 2 + TileLeftOffset;
        double midY = (fromTop + toTop) / 2 + TileTopOffset;
        double dx = midX - center, dy = midY - center;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1)
        {
            dx = toLeft + TileLeftOffset - center;
            dy = toTop + TileTopOffset - center;
            length = Math.Sqrt(dx * dx + dy * dy);
        }
        if (length < 1) return (midX - TileLeftOffset, midY - TileTopOffset);

        return (midX + dx / length * PinArcLift - TileLeftOffset,
                midY + dy / length * PinArcLift - TileTopOffset);
    }
}
