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

    private sealed record OverflowLayoutChoice(string Label, OverflowLayout Value);

    private static readonly OverflowLayoutChoice[] OverflowLayoutChoices =
    [
        new("None — one ring, classic wheel", OverflowLayout.None),
        new("Overflow band — inner ring stays, extras go outside", OverflowLayout.OverflowBand),
        new("Split balanced — two equal rings", OverflowLayout.SplitBalanced),
        new("Petals — compact, tiles alternate rings", OverflowLayout.Petals),
        new("Concentric columns — radial pairs", OverflowLayout.Columns),
    ];

    public SettingsWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        PaintAnimPreview();
        Shell.PrimaryClick += OnSave;
        var c = TargetStore.Config;
        foreach (var name in Themes.All.Keys) ThemeBox.Items.Add(name);
        foreach (var choice in OpenAnimationChoices) OpenAnimationBox.Items.Add(choice);
        OpenAnimationBox.DisplayMemberPath = nameof(OpenAnimationChoice.Label);
        ThemeBox.SelectedItem = Themes.All.ContainsKey(c.Theme) ? c.Theme : "Fluent";
        OpenAnimationBox.SelectedItem = OpenAnimationChoices.FirstOrDefault(x => x.Value == c.OpenAnimation)
            ?? OpenAnimationChoices[0];
        foreach (var choice in OverflowLayoutChoices) OverflowLayoutBox.Items.Add(choice);
        OverflowLayoutBox.DisplayMemberPath = nameof(OverflowLayoutChoice.Label);
        OverflowLayoutBox.SelectedItem = OverflowLayoutChoices.FirstOrDefault(x => x.Value == c.OverflowLayout)
            ?? OverflowLayoutChoices[0];
        OverflowThresholdBox.Text = WheelLayout.ClampThreshold(c.OverflowThreshold).ToString();
        OverflowLayoutBox.SelectionChanged += (_, _) => UpdateThresholdEnabled();
        UpdateThresholdEnabled();
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

    /// <summary>Colors the little orb preview from the current theme so it depicts the real wheel
    /// look rather than a fixed neon swatch: the backdrop and dots follow the theme, the frame the
    /// palette border. Dot opacities are set in XAML.</summary>
    private void PaintAnimPreview()
    {
        var th = Themes.Current;
        AnimPreviewBox.Background = new System.Windows.Media.SolidColorBrush(th.Backdrop);
        AnimPreviewBox.BorderBrush = Palettes.Border;
        var accent = new System.Windows.Media.SolidColorBrush(th.Accent);
        foreach (var dot in AnimPreviewDots.Children)
            if (dot is System.Windows.Shapes.Ellipse e) e.Fill = accent;
    }

    /// <summary>The threshold only applies to the overflow layouts, so it is greyed out for None.</summary>
    private void UpdateThresholdEnabled()
    {
        bool on = OverflowLayoutBox.SelectedItem is OverflowLayoutChoice { Value: not OverflowLayout.None };
        OverflowThresholdBox.IsEnabled = on;
        OverflowThresholdLabel.Opacity = on ? 1.0 : 0.5;
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
        // A non-empty numeric field that isn't a whole number was previously discarded silently, so the
        // dialog "saved" while quietly dropping the typo. Tell the user instead of losing their input.
        var invalid = new List<string>();
        CheckNumeric(OverflowThresholdBox.Text, "Overflow threshold", invalid);
        CheckNumeric(HoverBox.Text, "Hover delay", invalid);
        CheckNumeric(IdleBox.Text, "Idle fade", invalid);
        CheckNumeric(GroupShortcutDelayBox.Text, "Group shortcut delay", invalid);
        if (invalid.Count > 0)
        {
            MessageBox.Show(this,
                "These fields need a whole number:\n• " + string.Join("\n• ", invalid),
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ThemeBox.SelectedItem is string theme) c.Theme = theme;
        if (OpenAnimationBox.SelectedItem is OpenAnimationChoice animation) c.OpenAnimation = animation.Value;
        if (OverflowLayoutBox.SelectedItem is OverflowLayoutChoice overflow) c.OverflowLayout = overflow.Value;
        if (int.TryParse(OverflowThresholdBox.Text, out int threshold))
            c.OverflowThreshold = WheelLayout.ClampThreshold(threshold);
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
        try { StartupService.SetEnabled(AutostartBox.IsChecked == true); }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not change the 'start with Windows' setting", ex);
            MessageBox.Show(this,
                "Your other settings were saved, but the “start with Windows” option couldn't be changed.",
                "Autostart", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        (Owner as OverlayWindow)?.ApplySettings();
        Close();
    }

    private static void CheckNumeric(string text, string label, List<string> invalid)
    {
        var t = text.Trim();
        if (t.Length > 0 && !int.TryParse(t, out _)) invalid.Add(label); // empty means "keep current"
    }
}
