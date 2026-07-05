using System.Windows;
using System.Windows.Input;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private void PlaceWindow()
    {
        var wa = SystemParameters.WorkArea;
        double cx = TargetStore.Config.OrbX, cy = TargetStore.Config.OrbY;
        if (cx < 0 || cy < 0) { cx = wa.Right - 90; cy = wa.Top + wa.Height / 2; }
        Left = Math.Clamp(cx - HalfSize, wa.Left - HalfSize + 24, wa.Right - HalfSize - 24);
        Top  = Math.Clamp(cy - HalfSize, wa.Top  - HalfSize + 24, wa.Bottom - HalfSize - 24);
    }

    private void OnOrbMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CloseCloud();
            DragMove(); // блокирует до отпускания кнопки
            TargetStore.Config.OrbX = Left + HalfSize;
            TargetStore.Config.OrbY = Top + HalfSize;
            TargetStore.Save();
        }
        else ToggleCloud();
        e.Handled = true;
    }

    public void ToggleCloud() { if (_open) CloseCloud(); else OpenCloud(); }

    private void ShowToast(string msg)
    {
        ToastText.Text = msg;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OpenEditor(TargetItem t)
    {
        var dlg = new TargetEditorWindow(t) { Owner = this };
        dlg.ShowDialog();
        TargetStore.Save();
        if (_open) BuildCloud();
    }
}
