using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class SettingsWindow : Window
{
    private sealed record OpenAnimationChoice(string Label, OpenAnimation Value);

    private static readonly OpenAnimationChoice[] OpenAnimationChoices =
    [
        new("Pop", OpenAnimation.Pop),
        new("Radial burst", OpenAnimation.RadialBurst),
        new("Clock sweep", OpenAnimation.ClockSweep),
        new("Magnetic settle", OpenAnimation.MagneticSettle),
    ];

    public SettingsWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        var c = TargetStore.Config;
        foreach (var name in Themes.All.Keys) ThemeBox.Items.Add(name);
        foreach (var choice in OpenAnimationChoices) OpenAnimationBox.Items.Add(choice);
        OpenAnimationBox.DisplayMemberPath = nameof(OpenAnimationChoice.Label);
        ThemeBox.SelectedItem = Themes.All.ContainsKey(c.Theme) ? c.Theme : "Fluent";
        OpenAnimationBox.SelectedItem = OpenAnimationChoices.FirstOrDefault(x => x.Value == c.OpenAnimation)
            ?? OpenAnimationChoices[0];
        OpenAnimationSpeedSlider.Value = Math.Clamp(c.OpenAnimationSpeed, 0.5, 2.0);
        OpenAnimationSpeedText.Text = $"{OpenAnimationSpeedSlider.Value:0.##}x";
        OpenAnimationSpeedSlider.ValueChanged += (_, _) =>
            OpenAnimationSpeedText.Text = $"{OpenAnimationSpeedSlider.Value:0.##}x";
        ActionBox.SelectedIndex = c.GlobalAction == DropAction.Move ? 1 : 0;
        HoverBox.Text = c.HoverDelayMs.ToString();
        OpacitySlider.Value = c.OrbOpacity;
        IdleBox.Text = c.IdleFadeSeconds.ToString();
        HotkeyBox.Text = c.Hotkey;
        GroupShortcutDelayBox.Text = c.GroupShortcutDelayMs.ToString();
        DeduplicateBox.IsChecked = c.DeduplicateTargets;
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
        if (OpenAnimationBox.SelectedItem is OpenAnimationChoice animation) c.OpenAnimation = animation.Value;
        c.OpenAnimationSpeed = Math.Round(Math.Clamp(OpenAnimationSpeedSlider.Value, 0.5, 2.0), 2);
        c.GlobalAction = ActionBox.SelectedIndex == 1 ? DropAction.Move : DropAction.Copy;
        if (int.TryParse(HoverBox.Text, out int hover)) c.HoverDelayMs = Math.Clamp(hover, 50, 2000);
        c.OrbOpacity = Math.Round(OpacitySlider.Value, 2);
        if (int.TryParse(IdleBox.Text, out int idle)) c.IdleFadeSeconds = Math.Clamp(idle, 0, 3600);
        if (int.TryParse(GroupShortcutDelayBox.Text, out int sequenceDelay))
            c.GroupShortcutDelayMs = Math.Clamp(sequenceDelay, 150, 1500);
        if (hk.Length > 0) c.Hotkey = hk;
        c.DeduplicateTargets = DeduplicateBox.IsChecked == true;
        TargetStore.Save();
        try { StartupService.SetEnabled(AutostartBox.IsChecked == true); } catch { }
        (Owner as OverlayWindow)?.ApplySettings();
        Close();
    }
}
