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

    /// <summary>Grows the square window to hold the current wheel. Unlike a closed orb, an open
    /// wheel must be fully visible, so the whole window is clamped inside the virtual screen: the
    /// orb (wheel center) is nudged inward from a screen edge only as much as needed, so a wide
    /// overflow ring is never clipped. The orb returns to its resting spot on close (PlaceWindow).</summary>
    private void ApplyWheelWindow(double size)
    {
        if (Math.Abs(Width - size) > 0.5)
        {
            double cx = Left + Width / 2, cy = Top + Height / 2;
            Width = size;
            Height = size;
            double l = SystemParameters.VirtualScreenLeft, t = SystemParameters.VirtualScreenTop;
            double r = l + SystemParameters.VirtualScreenWidth, b = t + SystemParameters.VirtualScreenHeight;
            Left = SnapToPixel(Math.Clamp(cx - size / 2, l, Math.Max(l, r - size)), horizontal: true);
            Top = SnapToPixel(Math.Clamp(cy - size / 2, t, Math.Max(t, b - size)), horizontal: false);
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

    /// <summary>Shrinks the window back to BaseWindow when the wheel closes and returns the orb to
    /// its resting position — where only the small orb shows, a partly off-screen transparent
    /// window is fine, so the orb can hug a screen edge again.</summary>
    private void RestoreClosedWindow()
    {
        Width = BaseWindow;
        Height = BaseWindow;
        _wheelSize = BaseWindow;
        RecenterOrb();
        PlaceWindow();
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
        switch (OrbGesture.Classify(Keyboard.Modifiers))
        {
            case OrbDragKind.Capture:
                BeginOrbCapture(); // Alt+Shift: drag the orb onto a folder/app/file to pin it
                break;
            case OrbDragKind.Move:
                CloseCloud();
                _movingOrb = true;
                DragMove(); // blocks until the button is released
                _movingOrb = false;
                TargetStore.Config.OrbX = Left + HalfSize;
                TargetStore.Config.OrbY = Top + HalfSize;
                TargetStore.Save();
                UpdateOrbScreenPos();
                break;
            default:
                ToggleCloud();
                break;
        }
        e.Handled = true;
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
