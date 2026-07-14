using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private static readonly string[] HotkeyChipCombos =
    [
        AppConfig.DefaultHotkey,
        "Ctrl+Shift+Space",
        "Ctrl+Alt+D",
        "Ctrl+Shift+D",
        "Ctrl+Alt+F12",
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
    private TextBox? _recordingBox;

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
        HotkeyBox.Text = DisplayHotkey(c.Hotkey);
        OrbHotkeyBox.Text = c.HotkeyAtOrb.Length > 0 ? DisplayHotkey(c.HotkeyAtOrb) : "";
        GroupShortcutDelayBox.Text = c.GroupShortcutDelayMs.ToString();
        DeduplicateBox.IsChecked = c.DeduplicateTargets;
        AutostartBox.IsChecked = StartupService.IsEnabled;
        TextNameBox.Text = c.TextFileNameTemplate;
        CopyDestBox.IsChecked = c.CopyDestinationToClipboard;
        ToastSecondsBox.Text = c.ToastSeconds.ToString();
        ToastSoundBox.IsChecked = c.ToastSound;
        BuildTextNameChips();

        foreach (var box in new[] { HoverBox, OverflowThresholdBox, IdleBox, GroupShortcutDelayBox, ToastSecondsBox })
            box.TextChanged += (_, _) => QueueValidation();
        HotkeyBox.TextChanged += (_, _) => QueueValidation();
        HotkeyBox.PreviewKeyDown += OnRecordingKeyDown;
        OrbHotkeyBox.TextChanged += (_, _) => QueueValidation();
        OrbHotkeyBox.PreviewKeyDown += OnRecordingKeyDown;
        HotkeyCaptureButton.Click += (_, _) => ToggleRecording(HotkeyBox);
        OrbHotkeyCaptureButton.Click += (_, _) => ToggleRecording(OrbHotkeyBox);
        HotkeyResetButton.Click += OnHotkeyResetClick;
        HotkeyResetButton.ToolTip = $"Restore {AppConfig.DefaultHotkey}.";
        SectionList.SelectedIndex = 0;
        ValidateAll();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        for (int i = 0; i < _sections.Length; i++)
            _sections[i].Visibility = i == SectionList.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly string[] TextNameTokens = { "slug", "date", "time", "year", "month", "day" };

    /// <summary>Fills the chip row under the text-name box so the ${name} tokens are inserted by a click,
    /// sparing the user from mistyping the ${...} syntax.</summary>
    private void BuildTextNameChips()
    {
        TextNameChips.Children.Clear();
        foreach (var name in TextNameTokens)
        {
            var token = "${" + name + "}";
            var chip = new Border
            {
                Background = Palettes.Selection,
                BorderBrush = Palettes.TextMuted,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(6, 1, 6, 1),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = token, FontSize = 11, Foreground = Palettes.TextMuted },
                ToolTip = $"Insert {token} into the file name",
            };
            chip.MouseLeftButtonUp += (_, _) => InsertTextNameToken(token);
            TextNameChips.Children.Add(chip);
        }
    }

    private void InsertTextNameToken(string token)
    {
        int at = TextNameBox.SelectionStart;
        TextNameBox.Text = TextNameBox.Text.Insert(at, token);
        TextNameBox.SelectionStart = at + token.Length;
        TextNameBox.Focus();
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
        bool toast = ValidateNumber(ToastSecondsBox, ToastSecondsError);
        bool hotkey = ValidateHotkey();
        bool orbHotkey = ValidateOrbHotkey();
        WheelErrDot.Visibility = hover && threshold ? Visibility.Collapsed : Visibility.Visible;
        AppearanceErrDot.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        HotkeyErrDot.Visibility = hotkey && delay && orbHotkey ? Visibility.Collapsed : Visibility.Visible;
        Shell.IsPrimaryEnabled = hover && threshold && idle && delay && hotkey && orbHotkey && toast;
        BuildHotkeyChips();
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
        if (_recordingBox == HotkeyBox)
        {
            SetHotkeyStatus("Recording: press Ctrl, Alt, Shift, or Win plus one key", null);
            return false;
        }

        var hk = HotkeyBox.Text.Trim();
        if (hk.Length == 0) return SetHotkeyStatus("", null);
        if (!HotkeyService.TryNormalize(hk, out var normalized))
            return SetHotkeyStatus("Use Ctrl, Alt, Shift, or Win plus one key", false);
        if (HotkeyService.IsSameCombination(normalized, TargetStore.Config.Hotkey))
            return SetHotkeyStatus("Available", true);
        if (HotkeyService.IsAvailable(normalized)) return SetHotkeyStatus("Available", true);
        return SetHotkeyStatus("Already taken by another app", false);
    }

    private bool SetHotkeyStatus(string text, bool? ok)
    {
        HotkeyStatus.Text = text;
        HotkeyStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        HotkeyStatus.Foreground = ok switch
        {
            true => Palettes.Success,
            false => Palettes.Danger,
            _ => Palettes.TextMuted,
        };
        HotkeyBox.BorderBrush = ok == false ? Palettes.Danger
            : _recordingBox == HotkeyBox ? Palettes.Accent : Palettes.Border;
        return ok != false;
    }

    /// <summary>Live check for the optional second hotkey: empty disables it; it must differ from the
    /// primary combo (they would clash), parse, and be free right now. The currently saved second combo
    /// reads as available since we hold it.</summary>
    private bool ValidateOrbHotkey()
    {
        if (_recordingBox == OrbHotkeyBox)
        {
            SetOrbHotkeyStatus("Recording: press Ctrl, Alt, Shift, or Win plus one key", null);
            return false;
        }

        var hk = OrbHotkeyBox.Text.Trim();
        if (hk.Length == 0) return SetOrbHotkeyStatus("Off — no second hotkey", null);
        if (!HotkeyService.TryNormalize(hk, out var normalized))
            return SetOrbHotkeyStatus("Use Ctrl, Alt, Shift, or Win plus one key", false);
        if (HotkeyService.TryNormalize(HotkeyBox.Text.Trim(), out var primary)
            && HotkeyService.IsSameCombination(normalized, primary))
            return SetOrbHotkeyStatus("Same as the main hotkey", false);
        if (HotkeyService.IsSameCombination(normalized, TargetStore.Config.HotkeyAtOrb))
            return SetOrbHotkeyStatus("Available", true);
        if (HotkeyService.IsAvailable(normalized)) return SetOrbHotkeyStatus("Available", true);
        return SetOrbHotkeyStatus("Already taken by another app", false);
    }

    private bool SetOrbHotkeyStatus(string text, bool? ok)
    {
        OrbHotkeyStatus.Text = text;
        OrbHotkeyStatus.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        OrbHotkeyStatus.Foreground = ok switch
        {
            true => Palettes.Success,
            false => Palettes.Danger,
            _ => Palettes.TextMuted,
        };
        OrbHotkeyBox.BorderBrush = ok == false ? Palettes.Danger
            : _recordingBox == OrbHotkeyBox ? Palettes.Accent : Palettes.Border;
        return ok != false;
    }

    private void QueueValidation()
    {
        _validateTimer.Stop();
        _validateTimer.Start();
    }

    private void OnHotkeyResetClick(object sender, RoutedEventArgs e) =>
        SetHotkeyBoxText(HotkeyBox, AppConfig.DefaultHotkey);

    /// <summary>Starts recording into the box, or stops if it is already recording. Only one field
    /// records at a time, so starting one implicitly stops the other.</summary>
    private void ToggleRecording(TextBox box)
    {
        bool start = _recordingBox != box;
        StopRecording();
        if (!start) return;
        _recordingBox = box;
        RecordButtonFor(box).Content = "Stop";
        box.Focus();
        ValidateAll();
    }

    private void StopRecording()
    {
        if (_recordingBox == null) return;
        RecordButtonFor(_recordingBox).Content = "Record";
        _recordingBox = null;
        ValidateAll();
    }

    private Button RecordButtonFor(TextBox box) =>
        box == HotkeyBox ? HotkeyCaptureButton : OrbHotkeyCaptureButton;

    private void OnRecordingKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingBox == null || !ReferenceEquals(sender, _recordingBox)) return;

        e.Handled = true;
        var key = EffectiveKey(e);
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        if (HotkeyService.TryFormatCapturedHotkey(key, Keyboard.Modifiers, out var hotkey))
        {
            var box = _recordingBox;
            StopRecording();
            SetHotkeyBoxText(box, hotkey);
            return;
        }

        if (_recordingBox == HotkeyBox) SetHotkeyStatus("Press Ctrl, Alt, Shift, or Win plus one key", null);
        else SetOrbHotkeyStatus("Press Ctrl, Alt, Shift, or Win plus one key", null);
    }

    private void SetHotkeyBoxText(TextBox box, string hotkey)
    {
        StopRecording();
        box.Text = hotkey;
        ValidateAll();
    }

    /// <summary>How a suggestion chip relates to the hotkey field it belongs to: picked in it,
    /// held by the other hotkey field (clicking would just produce a clash), or free to pick.</summary>
    internal enum HotkeyChipKind { Normal, Selected, Conflicting }

    internal static HotkeyChipKind HotkeyChipState(string combo, string current, string other) =>
        HotkeyService.IsSameCombination(combo, current) ? HotkeyChipKind.Selected
        : HotkeyService.IsSameCombination(combo, other) ? HotkeyChipKind.Conflicting
        : HotkeyChipKind.Normal;

    /// <summary>Rebuilds both suggestion chip rows so the highlight follows the current field values.
    /// The second hotkey's row starts with an Off chip that clears it.</summary>
    private void BuildHotkeyChips()
    {
        FillHotkeyChipRow(HotkeyChips, HotkeyBox, OrbHotkeyBox, withOffChip: false);
        FillHotkeyChipRow(OrbHotkeyChips, OrbHotkeyBox, HotkeyBox, withOffChip: true);
    }

    private void FillHotkeyChipRow(WrapPanel row, TextBox target, TextBox other, bool withOffChip)
    {
        row.Children.Clear();
        if (withOffChip)
        {
            var kind = target.Text.Trim().Length == 0 ? HotkeyChipKind.Selected : HotkeyChipKind.Normal;
            row.Children.Add(MakeHotkeyChip("Off", kind, () => SetHotkeyBoxText(target, ""),
                "No second hotkey"));
        }
        foreach (var combo in HotkeyChipCombos)
        {
            var kind = HotkeyChipState(combo, target.Text.Trim(), other.Text.Trim());
            row.Children.Add(MakeHotkeyChip(combo, kind, () => SetHotkeyBoxText(target, combo),
                kind == HotkeyChipKind.Conflicting ? "Used by the other hotkey" : $"Use {combo}"));
        }
    }

    private static Border MakeHotkeyChip(string label, HotkeyChipKind kind, Action apply, string tooltip)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = kind switch
            {
                HotkeyChipKind.Selected => Palettes.Brush(Palettes.Current.AccentText),
                HotkeyChipKind.Conflicting => Palettes.TextMuted,
                _ => Palettes.Text,
            },
        };
        if (kind == HotkeyChipKind.Conflicting) text.TextDecorations = TextDecorations.Strikethrough;
        var chip = new Border
        {
            Background = kind == HotkeyChipKind.Selected ? Palettes.Accent : Palettes.Surface,
            BorderBrush = kind == HotkeyChipKind.Selected ? Palettes.Accent : Palettes.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 5, 5),
            Padding = new Thickness(9, 2, 9, 2),
            Child = text,
            ToolTip = tooltip,
        };
        if (kind != HotkeyChipKind.Conflicting)
        {
            chip.Cursor = Cursors.Hand;
            chip.MouseLeftButtonUp += (_, _) => apply();
        }
        return chip;
    }

    private static string DisplayHotkey(string hotkey) =>
        HotkeyService.TryNormalize(hotkey, out var normalized) ? normalized : hotkey;

    private static Key EffectiveKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key,
    };

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
        if (hk.Length > 0 && HotkeyService.TryNormalize(hk, out var normalizedHotkey))
            c.Hotkey = normalizedHotkey;
        var orbHk = OrbHotkeyBox.Text.Trim();
        c.HotkeyAtOrb = orbHk.Length > 0 && HotkeyService.TryNormalize(orbHk, out var orbNorm) ? orbNorm : "";
        c.DeduplicateTargets = DeduplicateBox.IsChecked == true;
        c.TextFileNameTemplate = TextNameBox.Text.Trim();
        c.CopyDestinationToClipboard = CopyDestBox.IsChecked == true;
        if (int.TryParse(ToastSecondsBox.Text, out int toastSeconds))
            c.ToastSeconds = Math.Clamp(toastSeconds, 1, 60);
        c.ToastSound = ToastSoundBox.IsChecked == true;
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
