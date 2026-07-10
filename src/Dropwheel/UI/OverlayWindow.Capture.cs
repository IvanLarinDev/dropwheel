using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Dropwheel.Models;
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

    /// <summary>A release closer than this to the orb's start counts as a click, not a capture,
    /// so a stray Alt+Shift click doesn't pin whatever sits behind the orb.</summary>
    private const double CaptureMinTravel = 48;

    private DispatcherTimer? _captureTimer;
    private double _captureHomeLeft, _captureHomeTop;

    /// <summary>Starts the Alt+Shift capture: the orb follows the cursor while a 60&#8239;Hz timer polls
    /// the mouse button and lights the pin ring over a valid target. Uses polling rather than mouse
    /// capture so the transparent overlay never has to win a hit-test against the window beneath it.</summary>
    private void BeginOrbCapture()
    {
        CloseCloud();
        _captureHomeLeft = Left;
        _captureHomeTop = Top;
        SetClickThrough(true);

        _captureTimer = new DispatcherTimer(DispatcherPriority.Input)
        { Interval = TimeSpan.FromMilliseconds(16) };
        _captureTimer.Tick += OnCaptureTick;
        _captureTimer.Start();
    }

    private void OnCaptureTick(object? sender, EventArgs e)
    {
        if (!GetCursorPos(out var p)) { FinishOrbCapture(); return; }

        Left = p.X - HalfSize;
        Top = p.Y - HalfSize;
        SetPinRing(CursorTargetLocator.LooksLikeTargetWindow(IsOwnWindow));

        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0) FinishOrbCapture();
    }

    /// <summary>Ends the capture: hides the overlay for one still probe so it can't shadow the
    /// target, resolves the path under the cursor, restores the orb to where it started, and pins
    /// whatever was found.</summary>
    private void FinishOrbCapture()
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        SetPinRing(false);

        string? path = null;
        if (GetCursorPos(out var p) && MovedFarEnough(p))
        {
            Visibility = Visibility.Hidden;
            path = CursorTargetLocator.ResolveUnderCursor(IsOwnWindow);
            Visibility = Visibility.Visible;
        }

        Left = _captureHomeLeft;
        Top = _captureHomeTop;
        SetClickThrough(false);
        UpdateOrbScreenPos();

        if (path != null) CaptureTarget(path);
    }

    /// <summary>Pins the captured path as a target on the current level, reusing the normal
    /// add-and-pin path so the tile flies in along the guided arc.</summary>
    private void CaptureTarget(string path)
    {
        var target = TargetFromPath(path);
        AddTargets(new[] { target }, _currentGroup, pinned: true);
    }

    private bool MovedFarEnough(POINT cursor)
        => IsCapture(cursor.X, cursor.Y, _captureHomeLeft + HalfSize, _captureHomeTop + HalfSize, CaptureMinTravel);

    /// <summary>True when the release point is far enough from the orb's start to count as a drag
    /// onto something rather than a click on the orb itself.</summary>
    internal static bool IsCapture(double cursorX, double cursorY, double homeCx, double homeCy, double minTravel)
        => Math.Sqrt(Math.Pow(cursorX - homeCx, 2) + Math.Pow(cursorY - homeCy, 2)) >= minTravel;

    private bool IsOwnWindow(IntPtr hwnd) => hwnd == new WindowInteropHelper(this).Handle;

    /// <summary>Toggles WS_EX_TRANSPARENT so WindowFromPoint reads the window under the overlay
    /// instead of the overlay itself while the orb is being dragged over a target.</summary>
    private void SetClickThrough(bool on)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        int updated = on ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (updated != style) SetWindowLong(hwnd, GWL_EXSTYLE, updated);
    }
}
