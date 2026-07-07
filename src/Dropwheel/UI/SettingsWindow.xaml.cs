using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        var c = TargetStore.Config;
        foreach (var name in Themes.All.Keys) ThemeBox.Items.Add(name);
        ThemeBox.SelectedItem = Themes.All.ContainsKey(c.Theme) ? c.Theme : "Fluent";
        ActionBox.SelectedIndex = c.GlobalAction == DropAction.Move ? 1 : 0;
        HoverBox.Text = c.HoverDelayMs.ToString();
        OpacitySlider.Value = c.OrbOpacity;
        IdleBox.Text = c.IdleFadeSeconds.ToString();
        HotkeyBox.Text = c.Hotkey;
        AutostartBox.IsChecked = StartupService.IsEnabled;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var c = TargetStore.Config;
        var hk = HotkeyBox.Text.Trim();
        // An empty field means "keep the current hotkey"; a non-empty but unrecognized one is an
        // input error: don't save and don't overwrite the working combo in config.
        if (hk.Length > 0 && !HotkeyService.IsValid(hk))
        {
            MessageBox.Show(this,
                "Could not recognize the key combination.\n" +
                "A valid example: Ctrl+Alt+Space. It needs modifier(s) and one key.",
                "Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (ThemeBox.SelectedItem is string theme) c.Theme = theme;
        c.GlobalAction = ActionBox.SelectedIndex == 1 ? DropAction.Move : DropAction.Copy;
        if (int.TryParse(HoverBox.Text, out int hover)) c.HoverDelayMs = Math.Clamp(hover, 50, 2000);
        c.OrbOpacity = Math.Round(OpacitySlider.Value, 2);
        if (int.TryParse(IdleBox.Text, out int idle)) c.IdleFadeSeconds = Math.Clamp(idle, 0, 3600);
        if (hk.Length > 0) c.Hotkey = hk;
        TargetStore.Save();
        try { StartupService.SetEnabled(AutostartBox.IsChecked == true); } catch { }
        (Owner as OverlayWindow)?.ApplySettings();
        Close();
    }
}
