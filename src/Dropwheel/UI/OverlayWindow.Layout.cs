using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private double _wheelSize = BaseWindow; // current square window side

    /// <summary>Grows or shrinks the square window to hold the current wheel, keeping the orb's
    /// screen center fixed so it never jumps under the pointer — a moving orb makes the hover/close
    /// logic oscillate the wheel open and closed. The window may extend past a screen edge (only the
    /// orb must stay reachable, so it is kept 24px inside); a wide overflow ring near an edge can then
    /// clip, which is accepted against the far worse flicker of a shifting orb. The size is BaseWindow
    /// while closed and larger only while an overflow level is open.</summary>
    private void ApplyWheelWindow(double size)
    {
        if (Math.Abs(Width - size) > 0.5)
        {
            double cx = Left + Width / 2, cy = Top + Height / 2;
            Width = size;
            Height = size;
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
            Left = SnapToPixel(Math.Clamp(cx - size / 2, l - size / 2 + 24, r - size / 2 - 24), horizontal: true);
            Top = SnapToPixel(Math.Clamp(cy - size / 2, t - size / 2 + 24, b - size / 2 - 24), horizontal: false);
        }
        _wheelSize = size;
        RecenterOrb();
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
        Left = Math.Clamp(cx - HalfSize, l - HalfSize + 24, r - HalfSize - 24);
        Top = Math.Clamp(cy - HalfSize, t - HalfSize + 24, b - HalfSize - 24);
    }

    private void OnOrbMouseDown(object sender, MouseButtonEventArgs e)
    {
        var kind = OrbGesture.Classify(Keyboard.Modifiers);
        ErrorLog.Trace($"orb-mousedown gesture={kind} mods={Keyboard.Modifiers}");
        _suppressProximity = true; // a press that starts on the orb is a click/gesture, not a drag approaching
        switch (kind)
        {
            case OrbDragKind.Capture:
                BeginOrbCapture(); // Alt+Shift: drag the orb onto a folder/app/file to pin it
                break;
            case OrbDragKind.Move:
                CloseCloud("orb-move");
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
        if (!TryCursorDip(out _dragStartCursor)) return;
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _orbDragged = false;
        _movingOrb = true;
        CaptureMouse();
        MouseMove += OnOrbDragMove;
        MouseLeftButtonUp += OnOrbDragEnd;
    }

    private void OnOrbDragMove(object sender, MouseEventArgs e)
    {
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

    private void OnOrbDragEnd(object sender, MouseButtonEventArgs e)
    {
        MouseMove -= OnOrbDragMove;
        MouseLeftButtonUp -= OnOrbDragEnd;
        ReleaseMouseCapture();
        _movingOrb = false;
        if (_orbDragged)
        {
            TargetStore.Config.OrbX = Left + HalfSize;
            TargetStore.Config.OrbY = Top + HalfSize;
            TargetStore.Save();
            ErrorLog.Trace($"orb-moved L={Left:0} T={Top:0}");
        }
        else ErrorLog.Trace("orb-move click (no move, orb stays)");
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

    public void ToggleCloud() { if (_open) CloseCloud("toggle"); else OpenCloud("toggle"); }

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
