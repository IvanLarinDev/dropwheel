using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
    private Ellipse? _ghostCore, _ghostHalo;
    private double _ghostArm, _ghostArmTarget;   // 0..1 arming as the ghost nears a valid target

    private Window? _hi;             // highlight drawn over the real target object
    private Border? _hiBorder;
    private long _lastProbe;         // throttles the expensive UI Automation hit-test
    private bool _probing;           // a background bounds probe is in flight
    private long _lastDiag;          // TEMP: throttles capture diagnostics

    /// <summary>Starts the Alt+Shift capture. The main orb stays put; a light ghost follows the
    /// cursor while a 60&#8239;Hz timer polls the mouse button and lights the ghost over a valid
    /// target. Polling avoids fighting a hit-test between the transparent overlay and the window
    /// beneath it.</summary>
    private void BeginOrbCapture()
    {
        ErrorLog.Write("orb capture: begin"); // TEMP
        CloseCloud();
        if (!GetCursorPos(out var p)) { ErrorLog.Write("orb capture: no cursor"); return; }

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

        bool armed = CursorTargetLocator.LooksLikeTargetWindow(IsOwnWindow);
        SetGhostArmed(armed);
        _ghostArm += (_ghostArmTarget - _ghostArm) * 0.25;
        ApplyGhostArm();
        UpdateTargetHighlight(armed, p);

        if (Environment.TickCount64 - _lastDiag > 300)
        {
            _lastDiag = Environment.TickCount64;
            ErrorLog.Write($"capture probe: armed={armed} class={CursorTargetLocator.DebugRootClassUnderCursor()}");
        }

        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) FinishOrbCapture();
    }

    /// <summary>On release, hides the ghost for one still probe so it can't shadow the target,
    /// resolves the path under the cursor, and either plays the capture sequence or dismisses the
    /// ghost when nothing valid was found.</summary>
    private void FinishOrbCapture()
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        CloseHighlight(); // gone before the still probe so it can't shadow the target
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
        RememberAdd(list);
        list.Add(target);
        TargetStore.PinToFront(list, target);
        TargetStore.Save();

        ShowToast($"Pinned: {target.Name}", canUndo: true);
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
        _ghostHalo = new Ellipse
        {
            Fill = new SolidColorBrush(th.Accent),
            IsHitTestVisible = false,
            Opacity = 0,
            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 },
        };
        var ball = new Ellipse
        {
            Fill = new SolidColorBrush(th.HubBg),
            Stroke = new SolidColorBrush(th.HubBorder),
            StrokeThickness = 1,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = 0.5 },
        };
        _ghostArm = _ghostArmTarget = 0;
        var grid = new System.Windows.Controls.Grid { Opacity = 0.9 };
        grid.Children.Add(_ghostHalo);
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

    private void SetGhostArmed(bool armed) => _ghostArmTarget = armed ? 1 : 0;

    /// <summary>Draws the eased arm level onto the ghost: the core swells and colours from the hub
    /// border toward the accent, and the halo blooms — the same "charge" the orb shows on the way in.</summary>
    private void ApplyGhostArm()
    {
        if (_ghostCore == null) return;
        var th = Themes.Current;
        _ghostCore.Fill = new SolidColorBrush(ColorLerp(th.HubBorder, th.Accent, _ghostArm));
        _ghostCore.Width = _ghostCore.Height = 18 + _ghostArm * 6;
        if (_ghostHalo != null)
        {
            _ghostHalo.Opacity = _ghostArm * 0.5;
            _ghostHalo.Width = _ghostHalo.Height = GhostSize * (0.7 + _ghostArm * 0.25);
        }
    }

    private static Color ColorLerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Lights a highlight over the real object under the cursor. The item's bounds come from
    /// UI Automation, which can block for a long time (badly so while the screen is being recorded),
    /// so the hit-test runs on a background thread and only the result is marshalled back — the UI
    /// thread never waits on it. One probe at a time, throttled; nothing valid hides the highlight.</summary>
    private void UpdateTargetHighlight(bool armed, POINT p)
    {
        if (!armed) { HideHighlight(); return; }
        if (_probing || Environment.TickCount64 - _lastProbe < 120) return;
        _probing = true;
        _lastProbe = Environment.TickCount64;
        int x = p.X, y = p.Y;
        System.Threading.Tasks.Task.Run(() =>
        {
            var bounds = TargetBounds(x, y);
            Dispatcher.BeginInvoke(() =>
            {
                _probing = false;
                if (_ghost == null) return; // capture ended while the probe was in flight
                if (bounds is { } rect) ShowHighlight(rect);
                else HideHighlight();
            });
        });
    }

    /// <summary>Screen bounds of the element under the point, or null when it can't be read or is an
    /// implausible size (empty, a sliver, or a whole-window hit that would frame nothing useful).</summary>
    private static Rect? TargetBounds(int x, int y)
    {
        try
        {
            var element = AutomationElement.FromPoint(new Point(x, y));
            if (element == null) return null;
            var r = element.Current.BoundingRectangle;
            if (r.IsEmpty || r.Width < 8 || r.Height < 8 || r.Width > 1600 || r.Height > 1200) return null;
            return r;
        }
        catch (Exception e) when (e is ElementNotAvailableException or COMException or TimeoutException or ArgumentException)
        {
            return null;
        }
    }

    private void ShowHighlight(Rect r)
    {
        var th = Themes.Current;
        if (_hi == null)
        {
            _hiBorder = new Border
            {
                BorderThickness = new Thickness(2.5),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
            };
            _hi = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Content = _hiBorder,
            };
            _hi.Show();
            SetClickThrough(_hi, true);
        }
        _hiBorder!.BorderBrush = new SolidColorBrush(th.Accent);
        _hi.Left = r.X - 3; _hi.Top = r.Y - 3;
        _hi.Width = r.Width + 6; _hi.Height = r.Height + 6;
        _hi.Visibility = Visibility.Visible;
    }

    private void HideHighlight()
    {
        if (_hi != null) _hi.Visibility = Visibility.Hidden;
    }

    private void CloseHighlight()
    {
        _hi?.Close();
        _hi = null;
        _hiBorder = null;
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
        CloseHighlight();
        _probing = false;
        _ghost?.Close();
        _ghost = null;
        _ghostCore = null;
        _ghostHalo = null;
    }

    private bool MovedFarEnough(POINT cursor)
        => IsCapture(cursor.X, cursor.Y, Left + HalfSize, Top + HalfSize, CaptureMinTravel);

    /// <summary>True when the release point is far enough from the orb to count as a drag onto
    /// something rather than a click on the orb itself.</summary>
    internal static bool IsCapture(double cursorX, double cursorY, double homeCx, double homeCy, double minTravel)
        => Math.Sqrt(Math.Pow(cursorX - homeCx, 2) + Math.Pow(cursorY - homeCy, 2)) >= minTravel;

    private bool IsOwnWindow(IntPtr hwnd)
        => hwnd == new WindowInteropHelper(this).Handle
           || (_ghost != null && hwnd == new WindowInteropHelper(_ghost).Handle)
           || (_hi != null && hwnd == new WindowInteropHelper(_hi).Handle);

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
