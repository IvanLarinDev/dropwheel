using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private const int VK_LBUTTON = 0x01;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    /// <summary>A release closer than this to the orb counts as a click, not a capture, so a stray
    /// Alt+Shift click doesn't pin whatever sits behind the orb.</summary>
    private const double CaptureMinTravel = 48;

    private const int GhostSize = 46;

    private DispatcherTimer? _captureTimer;
    private Window? _ghost;
    private Ellipse? _ghostCore;

    /// <summary>Starts the Alt+Shift capture. The main orb stays put; a light ghost follows the
    /// cursor while a 60&#8239;Hz timer polls the mouse button and lights the ghost over a valid
    /// target. Polling avoids fighting a hit-test between the transparent overlay and the window
    /// beneath it.</summary>
    private void BeginOrbCapture()
    {
        CloseCloud();
        if (!GetCursorPos(out var p)) return;

        SpawnGhost(p.X, p.Y);
        _captureTimer = new DispatcherTimer(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(16) };
        _captureTimer.Tick += OnCaptureTick;
        _captureTimer.Start();
    }

    private void OnCaptureTick(object? sender, EventArgs e)
    {
        if (_ghost == null || !GetCursorPos(out var p)) { FinishOrbCapture(); return; }

        _ghost.Left = p.X - GhostSize / 2.0;
        _ghost.Top = p.Y - GhostSize / 2.0;
        SetGhostArmed(CursorTargetLocator.LooksLikeTargetWindow(IsOwnWindow));

        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) FinishOrbCapture();
    }

    /// <summary>On release, hides the ghost for one still probe so it can't shadow the target,
    /// resolves the path under the cursor, and either plays the capture sequence or dismisses the
    /// ghost when nothing valid was found.</summary>
    private void FinishOrbCapture()
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        if (_ghost == null) return;

        string? path = null;
        if (GetCursorPos(out var p) && MovedFarEnough(p))
        {
            _ghost.Visibility = Visibility.Hidden;
            SetClickThrough(this, true); // let a target sitting under the main overlay be seen
            path = CursorTargetLocator.ResolveUnderCursor(IsOwnWindow);
            SetClickThrough(this, false);
            _ghost.Visibility = Visibility.Visible;
        }

        if (path != null) PlayCaptureSuccess(path);
        else DismissGhost();
    }

    /// <summary>Two-step confirm: the ghost flies back into the orb, the orb pulses a confirming
    /// ring, a beat, then the wheel opens and the captured tile arrives first.</summary>
    private void PlayCaptureSuccess(string path)
    {
        ReturnGhostToOrb(() =>
        {
            DismissGhost();
            PulsePinRing();
            var pause = TimeSpan.FromMilliseconds(ScaleTiming(300, AnimationSpeed()));
            var beat = new DispatcherTimer { Interval = pause };
            beat.Tick += (_, _) => { beat.Stop(); RevealCapturedTarget(path); };
            beat.Start();
        });
    }

    /// <summary>Adds the captured path pinned, opens the wheel already holding it, and animates it
    /// flying in from the hub.</summary>
    private void RevealCapturedTarget(string path)
    {
        var target = TargetFromPath(path);
        var list = _currentGroup?.Children ?? TargetStore.Config.Targets;
        list.Add(target);
        TargetStore.PinToFront(list, target);
        TargetStore.Save();

        ShowToast($"Pinned: {target.Name}");
        OpenCloud();
        AnimatePinnedArrival(new[] { target }, new Point(HalfSize, HalfSize));
        RefreshLinkMetadata(new[] { target });
    }

    private void SpawnGhost(int screenX, int screenY)
    {
        var th = Themes.Current;
        _ghostCore = new Ellipse
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new SolidColorBrush(th.Accent),
        };
        var ball = new Ellipse
        {
            Fill = new SolidColorBrush(th.HubBg),
            Stroke = new SolidColorBrush(th.HubBorder),
            StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.5 },
        };
        var grid = new System.Windows.Controls.Grid { Opacity = 0.9 };
        grid.Children.Add(ball);
        grid.Children.Add(_ghostCore);

        _ghost = new Window
        {
            Width = GhostSize,
            Height = GhostSize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Left = screenX - GhostSize / 2.0,
            Top = screenY - GhostSize / 2.0,
            Content = grid,
        };
        _ghost.Show();
        SetClickThrough(_ghost, true);
    }

    private void SetGhostArmed(bool armed)
    {
        if (_ghostCore == null) return;
        var th = Themes.Current;
        _ghostCore.Fill = new SolidColorBrush(armed ? th.Accent : th.HubBorder);
        _ghostCore.Width = _ghostCore.Height = armed ? 22 : 18;
    }

    /// <summary>Tweens the ghost from where it was released back to the orb centre, shrinking as it
    /// goes, then runs <paramref name="onArrived"/>.</summary>
    private void ReturnGhostToOrb(Action onArrived)
    {
        if (_ghost == null) { onArrived(); return; }

        double fromLeft = _ghost.Left, fromTop = _ghost.Top;
        double toLeft = Left + HalfSize - GhostSize / 2.0, toTop = Top + HalfSize - GhostSize / 2.0;
        var duration = TimeSpan.FromMilliseconds(ScaleTiming(180, AnimationSpeed()));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var start = DateTime.Now;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / duration.TotalMilliseconds);
            double e = ease.Ease(t);
            if (_ghost != null)
            {
                _ghost.Left = fromLeft + (toLeft - fromLeft) * e;
                _ghost.Top = fromTop + (toTop - fromTop) * e;
                _ghost.Opacity = 0.9 * (1 - e);
            }
            if (t >= 1) { timer.Stop(); onArrived(); }
        };
        timer.Start();
    }

    private void DismissGhost()
    {
        _ghost?.Close();
        _ghost = null;
        _ghostCore = null;
    }

    private bool MovedFarEnough(POINT cursor)
        => IsCapture(cursor.X, cursor.Y, Left + HalfSize, Top + HalfSize, CaptureMinTravel);

    /// <summary>True when the release point is far enough from the orb to count as a drag onto
    /// something rather than a click on the orb itself.</summary>
    internal static bool IsCapture(double cursorX, double cursorY, double homeCx, double homeCy, double minTravel)
        => Math.Sqrt(Math.Pow(cursorX - homeCx, 2) + Math.Pow(cursorY - homeCy, 2)) >= minTravel;

    private bool IsOwnWindow(IntPtr hwnd)
        => hwnd == new WindowInteropHelper(this).Handle
           || (_ghost != null && hwnd == new WindowInteropHelper(_ghost).Handle);

    /// <summary>Toggles a window's click-through (WS_EX_TRANSPARENT) so WindowFromPoint reads what
    /// is beneath it, not the window itself.</summary>
    private static void SetClickThrough(Window window, bool on)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        int updated = on ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (updated != style) SetWindowLong(hwnd, GWL_EXSTYLE, updated);
    }
}
