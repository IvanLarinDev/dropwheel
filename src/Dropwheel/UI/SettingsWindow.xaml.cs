using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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

    private readonly StackPanel[] _sections;
    private readonly DispatcherTimer _validateTimer;

    public SettingsWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        PaintAnimPreview();
        Shell.PrimaryClick += OnSave;
        _sections = new[] { WheelPanel, AppearancePanel, HotkeyPanel, SystemPanel };
        _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _validateTimer.Tick += (_, _) => { _validateTimer.Stop(); ValidateAll(); };

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
        OverflowLayoutBox.SelectionChanged += (_, _) => { UpdateThresholdEnabled(); ValidateAll(); };
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

        foreach (var box in new[] { HoverBox, OverflowThresholdBox, IdleBox, GroupShortcutDelayBox, HotkeyBox })
            box.TextChanged += (_, _) => { _validateTimer.Stop(); _validateTimer.Start(); };
        SectionList.SelectedIndex = 0;
        ValidateAll();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        for (int i = 0; i < _sections.Length; i++)
            _sections[i].Visibility = i == SectionList.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Validates every field, shows inline errors, marks the sections that hold a problem,
    /// and disables Save until all is well — so a bad value can never be saved and its section is
    /// findable even when hidden behind another one.</summary>
    private void ValidateAll()
    {
        bool hover = ValidateNumber(HoverBox, HoverError);
        bool threshold = !OverflowThresholdBox.IsEnabled || ValidateNumber(OverflowThresholdBox, ThresholdError);
        bool idle = ValidateNumber(IdleBox, IdleError);
        bool delay = ValidateNumber(GroupShortcutDelayBox, DelayError);
        bool hotkey = ValidateHotkey();
        WheelErrDot.Visibility = hover && threshold ? Visibility.Collapsed : Visibility.Visible;
        AppearanceErrDot.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        HotkeyErrDot.Visibility = hotkey && delay ? Visibility.Collapsed : Visibility.Visible;
        Shell.IsPrimaryEnabled = hover && threshold && idle && delay && hotkey;
    }

    /// <summary>A field that must hold a whole number. Empty is allowed and means "keep the current
    /// value", matching how save applies it.</summary>
    private static bool ValidateNumber(TextBox box, TextBlock error)
    {
        var t = box.Text.Trim();
        bool ok = t.Length == 0 || int.TryParse(t, out _);
        error.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        box.BorderBrush = ok ? Palettes.Border : Palettes.Danger;
        return ok;
    }

    /// <summary>Live hotkey check: empty keeps the current combo; a combo equal to the active one is
    /// always fine (we hold it, so a trial registration would wrongly report it taken); otherwise it
    /// must parse and be free right now.</summary>
    private bool ValidateHotkey()
    {
        var hk = HotkeyBox.Text.Trim();
        if (hk.Length == 0) return SetHotkeyStatus("", null);
        if (string.Equals(hk, TargetStore.Config.Hotkey, StringComparison.OrdinalIgnoreCase))
            return SetHotkeyStatus("Available", true);
        if (!HotkeyService.IsValid(hk))
            return SetHotkeyStatus("Not a valid combination — needs a modifier and one key", false);
        if (HotkeyService.IsAvailable(hk)) return SetHotkeyStatus("Available", true);
        return SetHotkeyStatus("Already taken by another app", false);
    }

    private bool SetHotkeyStatus(string text, bool? ok)
    {
        HotkeyStatus.Text = text;
        HotkeyStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        HotkeyStatus.Foreground = ok == true ? Palettes.Success : Palettes.Danger;
        HotkeyBox.BorderBrush = ok == false ? Palettes.Danger : Palettes.Border;
        return ok != false;
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
        // Save is disabled while any field is invalid, so validation has already passed here.
        var c = TargetStore.Config;
        var hk = HotkeyBox.Text.Trim();
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
            DwMessageBox.Show(this, "Start with Windows",
                "Your other settings were saved, but the “start with Windows” option couldn't be changed.");
        }
        (Owner as OverlayWindow)?.ApplySettings();
        Close();
    }
}
