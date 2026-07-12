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
    private const double TileLeftOffset = 38;
    private const double TileTopOffset = 40;
    private const double BaseWindow = 460;
    private TargetItem? _currentGroup;         // null = root level
    private TargetItem? _pendingGroup;
    private bool _pendingBack;
    private DispatcherTimer? _groupHover;
    private readonly Dictionary<FrameworkElement, Line> _spokes = new();

    private WheelCell[] _cells = [];           // one per tile, in display order (Back, targets, +)
    private Ellipse? _outerRim;                // the second rim band, only while overflow is open
    private ScaleTransform? _outerRimScale;
    private RotateTransform? _outerRimRot;

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
        TryShowOpenHint();
    }

    private void CloseCloud()
    {
        if (!_open)
        {
            ResetGroupShortcutInput();
            return;
        }
        _open = false;
        ResetGroupShortcutInput();
        _currentGroup = null;
        _groupHover?.Stop();
        Cloud.Children.Clear();
        _spokes.Clear();
        _outerRim = null;
        _cells = [];
        Rim.BeginAnimation(OpacityProperty, null);
        Rim.Opacity = 0;
        // The window keeps its fixed per-mode size; nothing to resize on close.
    }

    /// <summary>All tiles are laid out by the chosen overflow mode: one ring below the threshold,
    /// two rings above it. The last cell is always "+"; inside a group the first is "Back".
    /// When <paramref name="arriving"/> is given (an add onto the already-open level), tiles that were
    /// already present slide from their old positions in <paramref name="from"/> to their new slots
    /// instead of replaying the opening animation, and the arriving tiles fly in on an arc from
    /// <paramref name="origin"/> — so only the new tile visibly appears.</summary>
    private void BuildCloud(HashSet<TargetItem>? arriving = null,
        Dictionary<TargetItem, Point>? from = null, Point? origin = null)
    {
        Cloud.Children.Clear();
        _spokes.Clear();

        var source = CurrentLevelTargets();
        var items = new List<FrameworkElement>();
        if (_currentGroup != null) items.Add(MakeBackBubble());
        items.AddRange(TargetStore.OrderedForDisplay(source).Select(MakeBubble));
        items.Add(MakePlusTile());
        int n = items.Count;

        var mode = TargetStore.Config.OverflowLayout;
        int threshold = TargetStore.Config.OverflowThreshold;
        int reserved = (_currentGroup != null ? 1 : 0) + 1; // "Back" (in a group) and "+": not targets
        _cells = WheelLayout.Compute(mode, n, threshold, reserved);
        // The window is already fixed at the mode's max size (ApplyModeWindow), so opening never
        // resizes or moves it — the wheel simply draws centered in the space that is already there.

        // Invisible round backdrop covering the whole wheel: the mouse over the "empty" space inside
        // stays inside the window (otherwise switching group levels fired MouseLeave and the close
        // timer instantly). Clicking empty space closes.
        double bd = _wheelSize - 8;
        var backdrop = new Ellipse
        {
            Width = bd,
            Height = bd,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        };
        Canvas.SetLeft(backdrop, HalfSize - bd / 2);
        Canvas.SetTop(backdrop, HalfSize - bd / 2);
        backdrop.MouseLeftButtonUp += (_, e) => { CloseCloud(); e.Handled = true; };
        Cloud.Children.Add(backdrop);

        PositionRims(WheelLayout.RingRadii(mode, n, threshold, reserved));

        var th = Themes.Current;
        for (int i = 0; i < n; i++)
        {
            var slot = SlotFor(i);
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
            var slot = SlotFor(i);
            Canvas.SetLeft(items[i], slot.Left);
            Canvas.SetTop(items[i], slot.Top);
            Cloud.Children.Add(items[i]);
            if (arriving == null)
                AnimateTile(items[i], i, slot.Angle, _cells[i].Radius);
            else
                AnimateArrival(items[i], i, arriving, from, origin ?? new Point(HalfSize, HalfSize));
        }
        if (arriving == null) AnimateRim(); // an incremental add keeps the rim as-is
    }

    /// <summary>Rebuilds the open current level after an add so only the new tiles visibly appear: the
    /// arriving tiles fly in on an arc from the hub, tiles that were already there slide to their new
    /// slots, and the "+"/"Back" tiles just take their new place. Avoids the full opening replay.</summary>
    private void RebuildWithArrival(IReadOnlyList<TargetItem> arrived, Point origin)
    {
        var from = TileElementsByTarget().ToDictionary(
            kv => kv.Key, kv => new Point(Canvas.GetLeft(kv.Value), Canvas.GetTop(kv.Value)));
        BuildCloud(new HashSet<TargetItem>(arrived), from, origin);
    }

    private void AnimateArrival(FrameworkElement el, int i, HashSet<TargetItem> arriving,
        Dictionary<TargetItem, Point>? from, Point origin)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var target = el.Tag as TargetItem;
        if (target != null && arriving.Contains(target))
        {
            AnimateArcToSlot(el, origin, ScaleTiming(300, AnimationSpeed()), 0, ease); // new tile: arc from hub
            return;
        }
        if (target != null && from != null && from.TryGetValue(target, out var p) && !double.IsNaN(p.X))
        {
            Canvas.SetLeft(el, p.X);
            Canvas.SetTop(el, p.Y);
            AnimateAlongRim(el, _cells[i], ease, emphasize: false); // existing tile: slide old -> new slot
        }
        // "+"/"Back" and anything untracked: already placed at the slot; no entrance replay.
    }

    /// <summary>Places the pixel slot for tile <paramref name="i"/> from its precomputed cell.</summary>
    private WheelSlot SlotFor(int i) => SlotFrom(_cells[i]);

    private WheelSlot SlotFrom(WheelCell c) => new(
        c.Angle,
        HalfSize + c.Radius * Math.Cos(c.Angle) - TileLeftOffset,
        HalfSize + c.Radius * Math.Sin(c.Angle) - TileTopOffset,
        HalfSize + (c.Radius - 52) * Math.Cos(c.Angle),
        HalfSize + (c.Radius - 52) * Math.Sin(c.Angle));

    /// <summary>Maps each on-screen tile to its target. A target can transiently back more than one tile
    /// element during a rebuild, so a duplicate keeps the first element rather than throwing (a plain
    /// ToDictionary here would crash the reorder on a duplicate Tag).</summary>
    private Dictionary<TargetItem, FrameworkElement> TileElementsByTarget() =>
        Cloud.Children
            .OfType<FrameworkElement>()
            .Where(el => el.Tag is TargetItem)
            .GroupBy(el => (TargetItem)el.Tag!)
            .ToDictionary(g => g.Key, g => g.First());

    private void AnimateTileReorder(TargetItem moved)
    {
        var targets = TargetStore.OrderedForDisplay(CurrentLevelTargets()).ToArray();
        int offset = _currentGroup == null ? 0 : 1;

        var elements = TileElementsByTarget();

        // The reorder permutes the targets without changing the count, so the slot positions are the
        // ones already in _cells; each target just slides to the slot at its new display index. Rebuild
        // only if something is out of sync (a tile missing an element, or _cells not matching the count).
        if (targets.Any(target => !elements.ContainsKey(target))
            || _cells.Length != targets.Length + offset + 1) // != so a stale-longer _cells also rebuilds
        {
            BuildCloud();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            var element = elements[target];
            var cell = _cells[i + offset];
            bool isMoved = ReferenceEquals(target, moved);
            Panel.SetZIndex(element, isMoved ? 10 : 2);
            AnimateAlongRim(element, cell, ease, isMoved);
            if (_spokes.TryGetValue(element, out var spoke))
                AnimateSpokeAlongRim(spoke, cell, ease);
        }
    }

    private void AnimateAlongRim(FrameworkElement element, WheelCell cell, IEasingFunction ease, bool emphasize)
    {
        var slot = SlotFrom(cell);
        double fromLeft = Canvas.GetLeft(element);
        double fromTop = Canvas.GetTop(element);
        if (double.IsNaN(fromLeft)) fromLeft = slot.Left;
        if (double.IsNaN(fromTop)) fromTop = slot.Top;

        element.BeginAnimation(Canvas.LeftProperty, null);
        element.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(element, fromLeft);
        Canvas.SetTop(element, fromTop);

        var mid = MidArcSlot(fromLeft + TileLeftOffset, fromTop + TileTopOffset, cell);
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

    private void AnimateSpokeAlongRim(Line spoke, WheelCell cell, IEasingFunction ease)
    {
        var slot = SlotFrom(cell);
        double toR = cell.Radius - 52;
        double fromAngle = Math.Atan2(spoke.Y2 - HalfSize, spoke.X2 - HalfSize);
        double fromR = Math.Sqrt(Math.Pow(spoke.X2 - HalfSize, 2) + Math.Pow(spoke.Y2 - HalfSize, 2));
        double midAngle = fromAngle + NormalizeAngle(cell.Angle - fromAngle) / 2;
        double midR = (fromR + toR) / 2;
        double midX = HalfSize + midR * Math.Cos(midAngle);
        double midY = HalfSize + midR * Math.Sin(midAngle);
        var fromX = spoke.X2;
        var fromY = spoke.Y2;
        spoke.BeginAnimation(Line.X2Property, null);
        spoke.BeginAnimation(Line.Y2Property, null);
        spoke.X2 = fromX;
        spoke.Y2 = fromY;

        var duration = TimeSpan.FromMilliseconds(180);
        var x = ArcAnimation(midX, slot.SpokeX, duration, ease);
        var y = ArcAnimation(midY, slot.SpokeY, duration, ease);
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

    /// <summary>Midpoint of a tile's path to <paramref name="cell"/>: the half-way angle and half-way
    /// radius between where the tile is now (fromX, fromY — its center) and the target cell. Interpolating
    /// the radius too means a tile changing rings curves smoothly between them; a same-ring move stays an
    /// arc along that ring. Returned as a tile slot (its Left/Top carry the tile offset).</summary>
    private WheelSlot MidArcSlot(double fromX, double fromY, WheelCell cell)
    {
        double fromAngle = Math.Atan2(fromY - HalfSize, fromX - HalfSize);
        double fromR = Math.Sqrt(Math.Pow(fromX - HalfSize, 2) + Math.Pow(fromY - HalfSize, 2));
        double midAngle = fromAngle + NormalizeAngle(cell.Angle - fromAngle) / 2;
        double midR = (fromR + cell.Radius) / 2;
        return SlotFrom(new WheelCell(midAngle, midR));
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

    private void AnimateTile(FrameworkElement el, int index, double angle, double radius)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform();
        var tr = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { sc, tr } };
        el.Opacity = 0;

        var animation = TargetStore.Config.OpenAnimation;
        var speed = AnimationSpeed();
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
            OpenAnimation.RadialBurst => (-radius * 0.82 * Math.Cos(angle), -radius * 0.82 * Math.Sin(angle)),
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

    /// <summary>The user's animation speed, clamped to the range the wheel was tuned for.</summary>
    private static double AnimationSpeed()
        => Math.Clamp(TargetStore.Config.OpenAnimationSpeed, 0.5, 2.0);

    /// <summary>Sizes and positions the rim bands: the primary XAML Rim for the inner (or single)
    /// ring, and a second ellipse only when the level overflows onto an outer ring.</summary>
    private void PositionRims(IReadOnlyList<double> radii)
    {
        if (radii.Count == 0)
        {
            _outerRim = null;
            _outerRimScale = null;
            _outerRimRot = null;
            return;
        }
        var th = Themes.Current;
        double r0 = radii[0];
        Rim.Width = Rim.Height = r0 * 2;
        Canvas.SetLeft(Rim, HalfSize - r0);
        Canvas.SetTop(Rim, HalfSize - r0);
        Rim.Stroke = new SolidColorBrush(th.Rim);

        _outerRim = null;
        _outerRimScale = null;
        _outerRimRot = null;
        if (radii.Count <= 1) return;

        double r1 = radii[1];
        var scale = new ScaleTransform(0.7, 0.7);
        var rot = new RotateTransform(-10);
        var ellipse = new Ellipse
        {
            Width = r1 * 2,
            Height = r1 * 2,
            StrokeThickness = 34,
            Stroke = new SolidColorBrush(th.Rim),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, rot } },
        };
        Canvas.SetLeft(ellipse, HalfSize - r1);
        Canvas.SetTop(ellipse, HalfSize - r1);
        Cloud.Children.Add(ellipse); // added before spokes/tiles → renders behind them
        _outerRim = ellipse;
        _outerRimScale = scale;
        _outerRimRot = rot;
    }

    private void AnimateRim()
    {
        var th = Themes.Current;
        Rim.Stroke = new SolidColorBrush(th.Rim);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var speed = AnimationSpeed();
        Rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ScaleTiming(200, speed))));
        RimScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        RimScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        RimRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });

        if (_outerRim is null) return;
        _outerRim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ScaleTiming(200, speed))));
        _outerRimScale!.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        _outerRimScale!.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(ScaleTiming(280, speed))) { EasingFunction = ease });
        _outerRimRot!.BeginAnimation(RotateTransform.AngleProperty,
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
