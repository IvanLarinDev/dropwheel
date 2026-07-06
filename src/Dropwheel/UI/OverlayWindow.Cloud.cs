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
    private TargetItem? _currentGroup;         // null = root level
    private TargetItem? _pendingGroup;
    private bool _pendingBack;
    private DispatcherTimer? _groupHover;
    private readonly Dictionary<FrameworkElement, Line> _spokes = new();

    private void OpenCloud()
    {
        if (_open) return;
        _open = true;
        BuildCloud();
    }

    private void CloseCloud()
    {
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
            Width = 452, Height = 452,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
        };
        Canvas.SetLeft(backdrop, HalfSize - 226);
        Canvas.SetTop(backdrop, HalfSize - 226);
        backdrop.MouseLeftButtonUp += (_, e) => { CloseCloud(); e.Handled = true; };
        Cloud.Children.Add(backdrop);
        var source = _currentGroup?.Children ?? TargetStore.Config.Targets;
        var items = new List<FrameworkElement>();
        if (_currentGroup != null) items.Add(MakeBackBubble());
        items.AddRange(source.OrderByDescending(t => t.Pinned).Select(MakeBubble));
        items.Add(MakePlusTile());

        int n = items.Count;
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n;
            var spoke = new Line
            {
                X1 = HalfSize, Y1 = HalfSize,
                X2 = HalfSize + (RingR - 52) * Math.Cos(a),
                Y2 = HalfSize + (RingR - 52) * Math.Sin(a),
                Stroke = new SolidColorBrush(th.Spoke),
                StrokeThickness = 2, IsHitTestVisible = false,
            };
            Cloud.Children.Add(spoke);
            _spokes[items[i]] = spoke;
        }
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n;
            Canvas.SetLeft(items[i], HalfSize + RingR * Math.Cos(a) - 38);
            Canvas.SetTop(items[i], HalfSize + RingR * Math.Sin(a) - 40);
            Cloud.Children.Add(items[i]);
            AnimateTile(items[i], i * 24);
        }
        AnimateRim();
    }

    private static void AnimateTile(FrameworkElement el, int delayMs)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var sc = new ScaleTransform(0.4, 0.4);
        el.RenderTransform = sc;
        el.Opacity = 0;
        var d = TimeSpan.FromMilliseconds(delayMs);
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var grow = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260))
        { BeginTime = d, EasingFunction = ease };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        el.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { BeginTime = d });
    }

    private void AnimateRim()
    {
        var th = Themes.Current;
        Rim.Stroke = new SolidColorBrush(th.Rim);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Rim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        RimScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        RimScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        RimRot.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
    }

    private void SetSpokeLit(FrameworkElement el, bool on)
    {
        if (!_spokes.TryGetValue(el, out var s)) return;
        var th = Themes.Current;
        s.Stroke = new SolidColorBrush(on ? th.Accent : th.Spoke);
        s.StrokeThickness = on ? 2.5 : 2;
    }

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
