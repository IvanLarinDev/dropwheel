using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private double _wheelSize = BaseWindow; // current square window side (fixed per overflow mode)

    /// <summary>Sizes the window once for the active overflow mode and keeps it there for the whole
    /// session, so the window never resizes or moves while the wheel opens and closes — that motion,
    /// under a still pointer, is what made the hover/close logic glitch. The overflow modes reserve
    /// room for their widest ring up front; the empty margin is transparent and clicks pass through it.
    /// Called at startup and when settings change (both with the wheel closed).</summary>
    private void ApplyModeWindow()
    {
        double size = WheelLayout.MaxWindowSize(TargetStore.Config.OverflowLayout);
        if (Math.Abs(Width - size) > 0.5)
        {
            Width = size;
            Height = size;
        }
        _wheelSize = size;
        RecenterOrb();
        PlaceWindow(); // re-anchor the orb (window center) on its saved spot after any size change
    }

    /// <summary>Snaps a DIP window edge onto a whole device pixel. A transparent overlay placed on a
    /// fractional pixel (common at 125%/150% display scaling) renders blurry; the wheel and orb draw
    /// crisply when the window origin lands on the device-pixel grid.</summary>
    private double SnapToPixel(double dip, bool horizontal)
    {
        var s = VisualTreeHelper.GetDpi(this);
        double scale = horizontal ? s.DpiScaleX : s.DpiScaleY;
        return scale > 0 ? Math.Round(dip * scale) / scale : dip;
    }

    /// <summary>Re-centers the orb (and the toast) on the current window, so it stays at HalfSize
    /// after a resize. Called on every size change and when the wheel closes back to BaseWindow.</summary>
    private void RecenterOrb()
    {
        Canvas.SetLeft(Orb, HalfSize - Orb.Width / 2);
        Canvas.SetTop(Orb, HalfSize - Orb.Height / 2);
        Canvas.SetLeft(Toast, HalfSize - 140);
        Canvas.SetTop(Toast, _wheelSize - 48);
        UpdateOrbScreenPos();
        // After a resize/move, PointToScreen can read stale coordinates before the new layout is
        // committed, leaving the proximity zone offset from the real orb. Re-read once layout settles.
        Dispatcher.BeginInvoke(new Action(UpdateOrbScreenPos), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PlaceWindow()
    {
        double cx = TargetStore.Config.OrbX, cy = TargetStore.Config.OrbY;
        if (double.IsNaN(cx) || double.IsNaN(cy))
        {
            var wa = SystemParameters.WorkArea;
            cx = wa.Right - 90; cy = wa.Top + wa.Height / 2;
        }
        // bounds of the whole virtual screen — the orb may live on any monitor
        double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
        double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
        Left = SnapToPixel(Math.Clamp(cx - HalfSize, l - HalfSize + 24, r - HalfSize - 24), horizontal: true);
        Top = SnapToPixel(Math.Clamp(cy - HalfSize, t - HalfSize + 24, b - HalfSize - 24), horizontal: false);
    }

    private void OnOrbMouseDown(object sender, MouseButtonEventArgs e)
    {
        var kind = OrbGesture.Classify(Keyboard.Modifiers);
        _suppressProximity = true; // a press that starts on the orb is a click/gesture, not a drag approaching
        switch (kind)
        {
            case OrbDragKind.Capture:
                BeginOrbCapture(); // Alt+Shift: drag the orb onto a folder/app/file to pin it
                break;
            case OrbDragKind.Move:
                CloseCloud();
                BeginOrbDrag(); // manual move by cursor delta; avoids DragMove's per-monitor-DPI jump
                break;
            default:
                ToggleCloud();
                break;
        }
        e.Handled = true;
    }

    private Point _dragStartCursor;   // cursor at move start, DIP screen space
    private double _dragStartLeft, _dragStartTop;
    private bool _orbDragged;

    /// <summary>Starts a manual orb move driven by the real cursor delta. Unlike Window.DragMove(),
    /// which runs Win32's modal move loop and jumps the window under per-monitor DPI, this reads the
    /// cursor in device pixels and converts to DIP, so the orb follows the pointer exactly. The window
    /// only moves once the cursor passes the system drag threshold, so an Alt+click leaves it put.</summary>
    private void BeginOrbDrag()
    {
        if (_movingOrb) return; // already dragging — ignore a re-entrant press
        if (!TryCursorDip(out _dragStartCursor)) return;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _orbDragged = false;
        if (!CaptureMouse()) return; // can't grab the mouse → don't start a drag we couldn't end
        _movingOrb = true;
        MouseMove += OnOrbDragMove;
        MouseLeftButtonUp += OnOrbDragEnd;
        LostMouseCapture += OnOrbDragLostCapture;
    }

    private void OnOrbDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Released) { EndOrbDrag(); return; } // release we never got as a click
        if (!TryCursorDip(out var cur)) return;
        double dx = cur.X - _dragStartCursor.X, dy = cur.Y - _dragStartCursor.Y;
        if (!_orbDragged
            && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            return; // still within click slop — don't nudge the orb
        _orbDragged = true;
        Left = _dragStartLeft + dx;
        Top = _dragStartTop + dy;
    }

    private void OnOrbDragEnd(object sender, MouseButtonEventArgs e) => EndOrbDrag();
    private void OnOrbDragLostCapture(object sender, MouseEventArgs e) => EndOrbDrag();

    /// <summary>Ends the manual orb move exactly once, whichever way it finished: the button release, a
    /// release seen mid-move, or a stolen mouse capture (UAC prompt, Alt+Tab, lock screen). Without the
    /// LostMouseCapture path a stolen capture would leave <c>_movingOrb</c> stuck true, silently killing
    /// proximity, group shortcuts and idle-fade until restart. Idempotent; saves the orb's new resting
    /// position only if it was a real drag, not a click.</summary>
    private void EndOrbDrag()
    {
        if (!_movingOrb) return;
        MouseMove -= OnOrbDragMove;
        MouseLeftButtonUp -= OnOrbDragEnd;
        LostMouseCapture -= OnOrbDragLostCapture; // unsubscribe before releasing so the release doesn't re-enter
        _movingOrb = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_orbDragged)
        {
            TargetStore.Config.OrbX = Left + HalfSize;
            TargetStore.Config.OrbY = Top + HalfSize;
            TargetStore.Save();
        }
        UpdateOrbScreenPos();
    }

    /// <summary>Current mouse cursor in DIP screen coordinates, false if the visual has no source yet.</summary>
    private bool TryCursorDip(out Point dip)
    {
        dip = default;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } ct) return false;
        var c = System.Windows.Forms.Cursor.Position; // device px
        dip = ct.TransformFromDevice.Transform(new Point(c.X, c.Y));
        return true;
    }

    /// <summary>Saves the config, logging and swallowing a write failure instead of throwing. Used from
    /// the contexts where an uncaught exception is worst — a timer tick and startup — so a full disk or a
    /// permissions error leaves the app running (with in-memory state) rather than crashing. Returns
    /// false on failure so the caller can tell the user the change wasn't persisted.</summary>
    private static bool TrySaveConfig()
    {
        try { TargetStore.Save(); return true; }
        catch (Exception ex) { ErrorLog.Write("Failed to save config", ex); return false; }
    }

    public void ToggleCloud() { if (_open) CloseCloud(); else OpenCloud(); }

    /// <summary>Shows the user a short error message. Called from the global exception handler; the
    /// display itself is wrapped so it can't loop.</summary>
    public void NotifyError(string message)
    {
        try { ShowToast(message); } catch { /* toast unavailable — already logged */ }
    }

    private void ShowToast(string msg, bool canUndo = false)
    {
        ToastText.Text = msg;
        UndoLink.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(canUndo ? 6 : 3);
        _toastTimer.Start();
    }

    private void CreateGroup()
    {
        ResetGroupShortcutInput(preserveActivation: false);
        var p = new PromptWindow("New group", "Group name:") { Owner = this };
        if (p.ShowDialog() == true && p.Value.Trim() is { Length: > 0 } name)
        {
            TargetStore.Config.Targets.Add(new TargetItem
            {
                Name = name,
                Children = new(),
                GroupCode = TargetStore.NextAvailableGroupCode(),
            });
            TargetStore.Save();
            RefreshGroupShortcuts();
            if (_open) BuildCloud();
        }
    }

    private void OpenEditor(TargetItem t, TargetItem? preselectGroup = null)
    {
        ResetGroupShortcutInput(preserveActivation: false);
        var dlg = new TargetEditorWindow(t, preselectGroup) { Owner = this };
        dlg.ShowDialog();
        TargetStore.Save();
        RefreshGroupShortcuts();
        if (_open) BuildCloud();
    }

    /// <summary>Repaint the hub and rim with the current theme colors.</summary>
    private void PaintHub()
    {
        var th = Themes.Current;
        HubBall.Fill = new System.Windows.Media.SolidColorBrush(th.HubBg);
        HubBall.Stroke = new System.Windows.Media.SolidColorBrush(th.HubBorder);
        HubCore.Fill = new System.Windows.Media.RadialGradientBrush(th.Accent, th.HubBg);
        Halo.Fill = new System.Windows.Media.SolidColorBrush(th.Accent);
        var boltBrush = new System.Windows.Media.SolidColorBrush(th.HubBorder);
        Bolt1.Fill = Bolt2.Fill = Bolt3.Fill = Bolt4.Fill = boltBrush;
        Rim.Stroke = new System.Windows.Media.SolidColorBrush(th.Rim);
        PinRing.Stroke = new System.Windows.Media.SolidColorBrush(th.Accent);
    }
}
