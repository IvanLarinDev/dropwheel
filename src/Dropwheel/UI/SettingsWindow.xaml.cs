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
        // Пустое поле означает «оставить прежний хоткей»; непустой, но нераспознаваемый —
        // ошибка ввода: не сохраняем и не затираем рабочую комбинацию в конфиге.
        if (hk.Length > 0 && !HotkeyService.IsValid(hk))
        {
            MessageBox.Show(this,
                "Не удалось распознать сочетание клавиш.\n" +
                "Пример правильного: Ctrl+Alt+Space. Нужны модификатор(ы) и одна клавиша.",
                "Горячая клавиша", MessageBoxButton.OK, MessageBoxImage.Warning);
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
