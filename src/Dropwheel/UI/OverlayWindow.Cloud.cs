using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private const double RingR = 170;          // radius of the rim centerline
    private const double TileLeftOffset = 38;
    private const double TileTopOffset = 40;
    private TargetItem? _currentGroup;         // null = root level
    private TargetItem? _pendingGroup;
    private bool _pendingBack;
    private DispatcherTimer? _groupHover;
    private readonly Dictionary<FrameworkElement, Line> _spokes = new();

    private readonly record struct WheelSlot(
        double Angle,
        double Left,
        double Top,
        double SpokeX,
        double SpokeY);

    private void OpenCloud()
    {
        if (_open) return;
        _open = true;
        BuildCloud();
    }

    private void CloseCloud()
    {
        ResetGroupShortcutInput();
        if (!_open) return;
        _open = false;
        _currentGroup = null;
        _groupHover?.Stop();
        Cloud.Children.Clear();
        _spokes.Clear();
        Rim.BeginAnimation(OpacityProperty, null);
        Rim.Opacity = 0;
    }

    /// <summary>All tiles form one ring on the rim; the last cell is always "+".
    /// Inside a group the first tile is "Back".</summary>
    private void BuildCloud()
    {
        Cloud.Children.Clear();
        _spokes.Clear();
        var th = Themes.Current;

        // Invisible round backdrop: the mouse over the "empty" space inside the
        // wheel stays "inside the window" (otherwise switching group levels fired
        // MouseLeave and the close timer instantly). Clicking empty space closes.
        var backdrop = new System.Windows.Shapes.Ellipse
        {
            Width = 452,
            Height = 452,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        };
        Canvas.SetLeft(backdrop, HalfSize - 226);
        Canvas.SetTop(backdrop, HalfSize - 226);
        backdrop.MouseLeftButtonUp += (_, e) => { CloseCloud(); e.Handled = true; };
        Cloud.Children.Add(backdrop);
        var source = CurrentLevelTargets();
        var items = new List<FrameworkElement>();
        if (_currentGroup != null) items.Add(MakeBackBubble());
        items.AddRange(TargetStore.OrderedForDisplay(source).Select(MakeBubble));
        items.Add(MakePlusTile());

        int n = items.Count;
        for (int i = 0; i < n; i++)
        {
            var slot = SlotFor(i, n);
            var spoke = new Line
            {
                X1 = HalfSize,
                Y1 = HalfSize,
                X2 = slot.SpokeX,
                Y2 = slot.SpokeY,
                Stroke = new SolidColorBrush(th.Spoke),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Cloud.Children.Add(spoke);
            _spokes[items[i]] = spoke;
        }
        for (int i = 0; i < n; i++)
        {
            var slot = SlotFor(i, n);
            Canvas.SetLeft(items[i], slot.Left);
            Canvas.SetTop(items[i], slot.Top);
            Cloud.Children.Add(items[i]);
            AnimateTile(items[i], i, slot.Angle);
        }
        AnimateRim();
    }

    private static WheelSlot SlotFor(int index, int count)
    {
        double angle = -Math.PI / 2 + index * 2 * Math.PI / count;
        return new WheelSlot(
            angle,
            HalfSize + RingR * Math.Cos(angle) - TileLeftOffset,
            HalfSize + RingR * Math.Sin(angle) - TileTopOffset,
            HalfSize + (RingR - 52) * Math.Cos(angle),
            HalfSize + (RingR - 52) * Math.Sin(angle));
    }

    private void AnimateTileReorder(TargetItem moved)
    {
        var targets = TargetStore.OrderedForDisplay(CurrentLevelTargets()).ToArray();
        var elements = Cloud.Children
            .OfType<FrameworkElement>()
            .Where(el => el.Tag is TargetItem)
            .ToDictionary(el => (TargetItem)el.Tag);

        if (targets.Any(target => !elements.ContainsKey(target)))
        {
            BuildCloud();
            return;
        }

        int offset = _currentGroup == null ? 0 : 1;
        int count = targets.Length + offset + 1; // plus tile is still the last slot
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            var element = elements[target];
            var slot = SlotFor(i + offset, count);
            bool isMoved = ReferenceEquals(target, moved);
            Panel.SetZIndex(element, isMoved ? 10 : 2);
            AnimateAlongRim(element, slot, ease, isMoved);
            if (_spokes.TryGetValue(element, out var spoke))
                AnimateSpokeAlongRim(spoke, slot, ease);
        }
    }

    private static void AnimateAlongRim(FrameworkElement element, WheelSlot slot, IEasingFunction ease, bool emphasize)
    {
        double fromLeft = Canvas.GetLeft(element);
        double fromTop = Canvas.GetTop(element);
        if (double.IsNaN(fromLeft)) fromLeft = slot.Left;
        if (double.IsNaN(fromTop)) fromTop = slot.Top;

        element.BeginAnimation(Canvas.LeftProperty, null);
        element.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(element, fromLeft);
        Canvas.SetTop(element, fromTop);

        var mid = MidArcSlot(fromLeft + TileLeftOffset, fromTop + TileTopOffset, slot.Angle);
        var duration = TimeSpan.FromMilliseconds(emphasize ? 210 : 180);
        var left = ArcAnimation(mid.Left, slot.Left, duration, ease);
        var top = ArcAnimation(mid.Top, slot.Top, duration, ease);
        top.Completed += (_, _) =>
        {
            element.BeginAnimation(Canvas.LeftProperty, null);
            element.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(element, slot.Left);
            Canvas.SetTop(element, slot.Top);
            Panel.SetZIndex(element, 0);
        };
        element.BeginAnimation(Canvas.LeftProperty, left);
        element.BeginAnimation(Canvas.TopProperty, top);

        if (emphasize && TileScale(element) is { } scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 } });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 } });
        }
    }

    private static void AnimateSpokeAlongRim(Line spoke, WheelSlot slot, IEasingFunction ease)
    {
        var mid = MidArcSlot(spoke.X2, spoke.Y2, slot.Angle, RingR - 52);
        var fromX = spoke.X2;
        var fromY = spoke.Y2;
        spoke.BeginAnimation(Line.X2Property, null);
        spoke.BeginAnimation(Line.Y2Property, null);
        spoke.X2 = fromX;
        spoke.Y2 = fromY;

        var duration = TimeSpan.FromMilliseconds(180);
        var x = ArcAnimation(mid.Left, slot.SpokeX, duration, ease);
        var y = ArcAnimation(mid.Top, slot.SpokeY, duration, ease);
        y.Completed += (_, _) =>
        {
            spoke.BeginAnimation(Line.X2Property, null);
            spoke.BeginAnimation(Line.Y2Property, null);
            spoke.X2 = slot.SpokeX;
            spoke.Y2 = slot.SpokeY;
        };
        spoke.BeginAnimation(Line.X2Property, x);
        spoke.BeginAnimation(Line.Y2Property, y);
    }

    private static WheelSlot MidArcSlot(double fromX, double fromY, double toAngle, double radius = RingR)
    {
        double fromAngle = Math.Atan2(fromY - HalfSize, fromX - HalfSize);
        double midAngle = fromAngle + NormalizeAngle(toAngle - fromAngle) / 2;
        return new WheelSlot(
            midAngle,
            HalfSize + radius * Math.Cos(midAngle) - (radius == RingR ? TileLeftOffset : 0),
            HalfSize + radius * Math.Sin(midAngle) - (radius == RingR ? TileTopOffset : 0),
            HalfSize + (RingR - 52) * Math.Cos(midAngle),
            HalfSize + (RingR - 52) * Math.Sin(midAngle));
    }

    private static DoubleAnimationUsingKeyFrames ArcAnimation(
        double midValue,
        double finalValue,
        TimeSpan duration,
        IEasingFunction ease) => new()
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(midValue, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.52)))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } },
                new EasingDoubleKeyFrame(finalValue, KeyTime.FromTimeSpan(duration))
                { EasingFunction = ease },
            }
        };

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI) angle -= 2 * Math.PI;
        while (angle < -Math.PI) angle += 2 * Math.PI;
        return angle;
    }

    private static ScaleTransform? TileScale(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group)
            return group.Children.OfType<ScaleTransform>().FirstOrDefault();
        return element.RenderTransform as ScaleTransform;
    }

    private static void AnimateTile(FrameworkElement el, int index, double angle)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform();
        var tr = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { sc, tr } };
        el.Opacity = 0;

        var animation = TargetStore.Config.OpenAnimation;
        var speed = Math.Clamp(TargetStore.Config.OpenAnimationSpeed, 0.5, 2.0);
        var delayMs = ScaleTiming(animation switch
        {
            OpenAnimation.ClockSweep => index * 38,
            OpenAnimation.MagneticSettle => index * 12,
            OpenAnimation.RadialBurst => index * 18,
            _ => index * 18,
        }, speed);
        var durationMs = ScaleTiming(animation switch
        {
            OpenAnimation.ClockSweep => 190,
            OpenAnimation.MagneticSettle => 170,
            OpenAnimation.RadialBurst => 240,
            _ => 220,
        }, speed);
        var startScale = animation switch
        {
            OpenAnimation.RadialBurst => 0.46,
            OpenAnimation.ClockSweep => 0.68,
            OpenAnimation.MagneticSettle => 0.86,
            _ => 0.72,
        };
        var (startX, startY) = animation switch
        {
            OpenAnimation.RadialBurst => (-RingR * 0.82 * Math.Cos(angle), -RingR * 0.82 * Math.Sin(angle)),
            OpenAnimation.ClockSweep => (-18 * Math.Sin(angle), 18 * Math.Cos(angle)),
            OpenAnimation.MagneticSettle => (-10 * Math.Cos(angle), -10 * Math.Sin(angle)),
            _ => (-24 * Math.Cos(angle), -24 * Math.Sin(angle)),
        };

        sc.ScaleX = sc.ScaleY = startScale;
        tr.X = startX;
        tr.Y = startY;

        var d = TimeSpan.FromMilliseconds(delayMs);
        IEasingFunction ease = animation switch
        {
            OpenAnimation.MagneticSettle => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 },
            OpenAnimation.RadialBurst => new CubicEase { EasingMode = EasingMode.EaseOut },
            OpenAnimation.ClockSweep => new SineEase { EasingMode = EasingMode.EaseOut },
            _ => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.36 },
        };
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var growX = new DoubleAnimation(startScale, 1, duration) { BeginTime = d, EasingFunction = ease };
        var growY = new DoubleAnimation(startScale, 1, duration) { BeginTime = d, EasingFunction = ease };
        var moveX = new DoubleAnimation(startX, 0, duration) { BeginTime = d, EasingFunction = ease };
        var moveY = new DoubleAnimation(startY, 0, duration) { BeginTime = d, EasingFunction = ease };

        sc.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, growY);
        tr.BeginAnimation(TranslateTransform.XProperty, moveX);
        tr.BeginAnimation(TranslateTransform.YProperty, moveY);
        var opacityMs = ScaleTiming(animation == OpenAnimation.ClockSweep ? 120 : 140, speed);
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(opacityMs))
            { BeginTime = d });
    }

    private static int ScaleTiming(int milliseconds, double speed)
        => Math.Max(40, (int)Math.Round(milliseconds / speed));

    private void AnimateRim()
    {
        var th = Themes.Current;
        Rim.Stroke = new SolidColorBrush(th.Rim);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var speed = Math.Clamp(TargetStore.Config.OpenAnimationSpeed, 0.5, 2.0);
        Rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ScaleTiming(200, speed))));
        RimScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        RimScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        RimRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
    }

    private void SetSpokeLit(FrameworkElement el, bool on)
    {
        if (!_spokes.TryGetValue(el, out var s)) return;
        var th = Themes.Current;
        s.Stroke = new SolidColorBrush(on ? th.Accent : th.Spoke);
        s.StrokeThickness = on ? 2.5 : 2;
    }

    private IList<TargetItem> CurrentLevelTargets() => _currentGroup?.Children ?? TargetStore.Config.Targets;

    private void EnterGroup(TargetItem? group)
    {
        _currentGroup = group;
        _closeTimer.Stop(); // navigation is not mouse-leave
        if (_open) BuildCloud();
    }

    private void StartGroupHover(TargetItem? group, bool back)
    {
        if (_groupHover == null)
        {
            _groupHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _groupHover.Tick += (_, _) =>
            {
                _groupHover!.Stop();
                EnterGroup(_pendingBack ? null : _pendingGroup);
            };
        }
        _pendingGroup = group; _pendingBack = back;
        if (!_groupHover.IsEnabled) _groupHover.Start();
    }
}
