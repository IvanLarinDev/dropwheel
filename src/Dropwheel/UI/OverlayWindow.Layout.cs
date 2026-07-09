using System.Windows;
using System.Windows.Input;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CloseCloud();
            _movingOrb = true;
            DragMove(); // blocks until the button is released
            _movingOrb = false;
            TargetStore.Config.OrbX = Left + HalfSize;
            TargetStore.Config.OrbY = Top + HalfSize;
            TargetStore.Save();
            UpdateOrbScreenPos();
        }
        else ToggleCloud();
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
        var p = new PromptWindow("New group", "Group name:") { Owner = this };
        if (p.ShowDialog() == true && p.Value.Trim() is { Length: > 0 } name)
        {
            TargetStore.Config.Targets.Add(new TargetItem { Name = name, Children = new() });
            TargetStore.Save();
            if (_open) BuildCloud();
        }
    }

    private void OpenEditor(TargetItem t, TargetItem? preselectGroup = null)
    {
        var dlg = new TargetEditorWindow(t, preselectGroup) { Owner = this };
        dlg.ShowDialog();
        TargetStore.Save();
        if (_open) BuildCloud();
    }

    /// <summary>Repaint the hub and rim with the current theme colors.</summary>
    private void PaintHub()
    {
        var th = Themes.Current;
        HubBall.Fill = new System.Windows.Media.SolidColorBrush(th.HubBg);
        HubBall.Stroke = new System.Windows.Media.SolidColorBrush(th.HubBorder);
        HubCore.Fill = new System.Windows.Media.RadialGradientBrush(th.Accent, th.HubBg);
        var boltBrush = new System.Windows.Media.SolidColorBrush(th.HubBorder);
        Bolt1.Fill = Bolt2.Fill = Bolt3.Fill = Bolt4.Fill = boltBrush;
        Rim.Stroke = new System.Windows.Media.SolidColorBrush(th.Rim);
    }
}
