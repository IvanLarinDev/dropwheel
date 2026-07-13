using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private enum ConfidenceTone { Info, Success, Warning, Danger }

    private readonly record struct DropConfidencePreview(
        DragDropEffects Effects,
        string BadgeText,
        string ChipText,
        string StatusText,
        ConfidenceTone Tone,
        bool CanDrop,
        string? ActiveLabelText = null,
        DropPayloadKind PayloadKind = DropPayloadKind.Unsupported);

    private sealed class ConfidenceVisuals
    {
        public required Border Ring { get; init; }
        public required Border Chip { get; init; }
        public required TextBlock ChipText { get; init; }
        public TextBlock? Label { get; init; }
        public Border? Badge { get; init; }
        public required string BaseAutomationName { get; init; }
        public Action? KeyboardActivate { get; init; }
        public TargetItem? Target { get; init; }
        public string KeyboardStatus { get; init; } = "";
        public double BaseLabelMaxWidth { get; init; }
        public double BaseLabelFontSize { get; init; }
        public FontWeight BaseLabelWeight { get; init; }
        public string BaseLabelText { get; init; } = "";
    }

    private readonly Dictionary<FrameworkElement, ConfidenceVisuals> _confidenceVisuals = new();
    private readonly List<FrameworkElement> _keyboardTargets = new();
    private FrameworkElement? _confidenceElement;
    private int _keyboardTargetIndex = -1;
    private string _lastConfidenceAnnouncement = "";

    private (Border Ring, Border Chip, TextBlock ChipText) MakeConfidenceOverlay()
    {
        var ring = new Border
        {
            Width = 68,
            Height = 68,
            CornerRadius = new CornerRadius(19),
            BorderThickness = new Thickness(2.4),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chipText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 110,
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 1, 6, 2),
            BorderThickness = new Thickness(1),
            MinWidth = 42,
            MaxWidth = 126,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1),
            Child = chipText,
        };
        return (ring, chip, chipText);
    }

    private void RegisterConfidenceVisuals(
        FrameworkElement element,
        Border ring,
        Border chip,
        TextBlock chipText,
        TextBlock? label,
        string baseAutomationName,
        Action? keyboardActivate,
        string keyboardStatus,
        Border? badge = null,
        TargetItem? target = null)
    {
        element.Focusable = true;
        _confidenceVisuals[element] = new ConfidenceVisuals
        {
            Ring = ring,
            Chip = chip,
            ChipText = chipText,
            Label = label,
            Badge = badge,
            BaseAutomationName = baseAutomationName,
            KeyboardActivate = keyboardActivate,
            Target = target,
            KeyboardStatus = keyboardStatus,
            BaseLabelMaxWidth = label?.MaxWidth ?? 0,
            BaseLabelFontSize = label?.FontSize ?? 0,
            BaseLabelWeight = label?.FontWeight ?? FontWeights.Normal,
            BaseLabelText = label?.Text ?? "",
        };
        _keyboardTargets.Add(element);
    }

    private void ResetConfidenceRegistry()
    {
        _confidenceVisuals.Clear();
        _keyboardTargets.Clear();
        _confidenceElement = null;
        _keyboardTargetIndex = -1;
        _lastConfidenceAnnouncement = "";
    }

    private void ShowDropConfidence(FrameworkElement element, TargetItem target, Border badge, DropConfidencePreview preview)
    {
        if (badge.Child is TextBlock badgeText) badgeText.Text = preview.BadgeText;
        badge.Background = ConfidenceBrush(preview.Tone);
        badge.Visibility = Visibility.Visible;

        var name = AccessibleName(target);
        var status = $"{name}. {preview.StatusText}";
        ShowConfidence(
            element,
            preview.ChipText,
            status,
            preview.Tone,
            preview.CanDrop,
            preview.ActiveLabelText,
            preview.PayloadKind);
    }

    private void ShowGeneralConfidence(
        FrameworkElement element,
        string chipText,
        string statusText,
        ConfidenceTone tone,
        bool canDrop = true,
        string? activeLabelText = null)
    {
        ShowConfidence(element, chipText, statusText, tone, canDrop, activeLabelText);
    }

    private void ShowConfidence(
        FrameworkElement element,
        string chipText,
        string statusText,
        ConfidenceTone tone,
        bool canDrop,
        string? activeLabelText = null,
        DropPayloadKind? payloadKind = null)
    {
        if (!_confidenceVisuals.TryGetValue(element, out var visuals)) return;

        _confidenceElement = element;
        var toneBrush = ConfidenceBrush(tone);
        foreach (var (candidate, candidateVisuals) in _confidenceVisuals)
        {
            bool active = ReferenceEquals(candidate, element);
            candidate.Opacity = active ? 1.0 : CandidateOpacity(candidateVisuals, canDrop, payloadKind);
            candidateVisuals.Ring.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            candidateVisuals.Ring.Opacity = active ? 1 : 0;
            candidateVisuals.Chip.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (!active) RestoreLabel(candidateVisuals);
        }

        visuals.Ring.BorderBrush = toneBrush;
        visuals.Ring.BorderThickness = new Thickness(canDrop ? 2.4 : 3.0);
        visuals.Chip.Background = new SolidColorBrush(Themes.Current.Backdrop);
        visuals.Chip.BorderBrush = toneBrush;
        visuals.ChipText.Text = chipText;
        visuals.Badge?.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        EmphasizeLabel(visuals, activeLabelText);

        AutomationProperties.SetName(element, statusText);
        AutomationProperties.SetHelpText(element, chipText);
        AnnounceConfidence(statusText, tone == ConfidenceTone.Danger || !canDrop);
    }

    private static double CandidateOpacity(
        ConfidenceVisuals visuals,
        bool activeCanDrop,
        DropPayloadKind? payloadKind)
    {
        if (visuals.Target == null
            || payloadKind is not { } payload
            || payload == DropPayloadKind.Unsupported)
            return activeCanDrop ? 0.42 : 0.58;

        var targetKind = DropIntent.ClassifyTarget(
            visuals.Target,
            LaunchService.IsFolderTarget(visuals.Target),
            LaunchService.IsRunTarget(visuals.Target));
        var compatibility = DropIntent.Compatibility(payload, targetKind);
        if (!compatibility.CanReceive) return 0.18;
        return compatibility.Level == DropCompatibilityLevel.Caution ? 0.52 : 0.42;
    }

    private void ClearConfidenceTarget()
    {
        if (_confidenceElement == null && _confidenceVisuals.Count == 0) return;
        foreach (var (element, visuals) in _confidenceVisuals)
        {
            element.Opacity = 1.0;
            visuals.Ring.Visibility = Visibility.Collapsed;
            visuals.Ring.Opacity = 0;
            visuals.Chip.Visibility = Visibility.Collapsed;
            RestoreLabel(visuals);
            AutomationProperties.SetName(element, visuals.BaseAutomationName);
            AutomationProperties.SetHelpText(element, "");
        }
        _confidenceElement = null;
        _lastConfidenceAnnouncement = "";
    }

    private void ClearConfidenceTarget(FrameworkElement element)
    {
        if (ReferenceEquals(_confidenceElement, element)) ClearConfidenceTarget();
    }

    private void EmphasizeLabel(ConfidenceVisuals visuals, string? activeLabelText)
    {
        if (visuals.Label == null) return;
        if (!string.IsNullOrWhiteSpace(activeLabelText))
            visuals.Label.Text = activeLabelText;
        visuals.Label.FontWeight = FontWeights.SemiBold;
        visuals.Label.FontSize = Math.Max(visuals.BaseLabelFontSize, 12.5);
        visuals.Label.MaxWidth = Math.Max(visuals.BaseLabelMaxWidth, 132);
    }

    private static void RestoreLabel(ConfidenceVisuals visuals)
    {
        if (visuals.Label == null) return;
        visuals.Label.Text = visuals.BaseLabelText;
        visuals.Label.FontWeight = visuals.BaseLabelWeight;
        visuals.Label.FontSize = visuals.BaseLabelFontSize;
        visuals.Label.MaxWidth = visuals.BaseLabelMaxWidth;
    }

    private void AnnounceConfidence(string message, bool assertive)
    {
        if (message == _lastConfidenceAnnouncement) return;
        _lastConfidenceAnnouncement = message;
        AutomationProperties.SetLiveSetting(ConfidenceLiveText,
            assertive ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        ConfidenceLiveText.Text = message;
        var peer = UIElementAutomationPeer.FromElement(ConfidenceLiveText)
                   ?? UIElementAutomationPeer.CreatePeerForElement(ConfidenceLiveText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static Brush ConfidenceBrush(ConfidenceTone tone) => tone switch
    {
        ConfidenceTone.Success => Palettes.Success,
        ConfidenceTone.Warning => Palettes.Warning,
        ConfidenceTone.Danger => Palettes.Danger,
        _ => Palettes.Info,
    };

    private void OnOverlayPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_open) return;

        if (e.Key == Key.Escape)
        {
            CloseCloud();
            e.Handled = true;
            return;
        }

        if (_keyboardTargets.Count == 0) return;

        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
            case Key.Tab when Keyboard.Modifiers != ModifierKeys.Shift:
                MoveKeyboardTarget(1);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
            case Key.Tab:
                MoveKeyboardTarget(-1);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                ActivateKeyboardTarget();
                e.Handled = true;
                break;
        }
    }

    private void MoveKeyboardTarget(int delta)
    {
        if (_keyboardTargets.Count == 0) return;
        _keyboardTargetIndex = _keyboardTargetIndex < 0
            ? (delta > 0 ? 0 : _keyboardTargets.Count - 1)
            : (_keyboardTargetIndex + delta + _keyboardTargets.Count) % _keyboardTargets.Count;

        var element = _keyboardTargets[_keyboardTargetIndex];
        if (!_confidenceVisuals.TryGetValue(element, out var visuals)) return;
        Keyboard.Focus(element);
        ShowConfidence(
            element,
            "Focus",
            visuals.KeyboardStatus.Length > 0 ? visuals.KeyboardStatus : visuals.BaseAutomationName,
            ConfidenceTone.Info,
            canDrop: true);
    }

    private void ActivateKeyboardTarget()
    {
        if (_keyboardTargetIndex < 0)
        {
            MoveKeyboardTarget(1);
            return;
        }

        var element = _keyboardTargets[_keyboardTargetIndex];
        if (_confidenceVisuals.TryGetValue(element, out var visuals))
            visuals.KeyboardActivate?.Invoke();
    }
}
