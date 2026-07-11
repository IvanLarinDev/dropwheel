using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private readonly GroupShortcutSequence _groupShortcutSequence = new();
    private readonly GroupShortcutActivation _groupShortcutActivation = new();
    private DispatcherTimer? _groupShortcutTimer;
    private KeyboardHook? _groupKeyboardHook;

    private static TimeSpan GroupShortcutInterval() => TimeSpan.FromMilliseconds(
        Math.Clamp(TargetStore.Config.GroupShortcutDelayMs, 150, 1500));

    private void InitGroupShortcuts()
    {
        _groupShortcutTimer = new DispatcherTimer { Interval = GroupShortcutInterval() };
        _groupShortcutTimer.Tick += (_, _) => OnGroupShortcutTimeout();
        _groupKeyboardHook = new KeyboardHook(OnGroupShortcutDigit);
        _groupKeyboardHook.Start();
        RefreshGroupShortcuts();
        Closed += (_, _) =>
        {
            _groupShortcutTimer?.Stop();
            _groupKeyboardHook?.Dispose();
        };
    }

    private void ArmGroupShortcuts()
    {
        _groupShortcutActivation.PointerEntered();
        RefreshGroupShortcuts();
    }

    private void OnOrbGroupShortcutLeave()
    {
        _groupShortcutActivation.PointerLeft(
            wheelOpen: _open,
            inputPending: _groupShortcutSequence.Input.Length > 0);
    }

    private void RefreshGroupShortcuts()
    {
        var codes = TargetStore.Groups.Select(group => group.GroupCode).ToArray();
        _groupShortcutSequence.SetCodes(codes);
        _groupShortcutActivation.Refresh(codes.Any(GroupShortcutSequence.IsValidCode));
        HideGroupShortcutInput();
    }

    private void ApplyGroupShortcutSettings()
    {
        if (_groupShortcutTimer != null) _groupShortcutTimer.Interval = GroupShortcutInterval();
        RefreshGroupShortcuts();
    }

    private bool OnGroupShortcutDigit(char digit)
    {
        var inputPending = _groupShortcutSequence.Input.Length > 0;
        if (!_open && !inputPending && !Orb.IsMouseOver)
            _groupShortcutActivation.PointerLeft(wheelOpen: false, inputPending: false);
        if (!_groupShortcutActivation.CanAcceptDigit(
                wheelOpen: _open,
                inputPending: inputPending)
            || !IsVisible || !IsEnabled || _hiddenByFullscreen || _movingOrb)
            return false;
        if (Orb.ContextMenu?.IsOpen == true) return false;
        if (Keyboard.Modifiers != ModifierKeys.None || Mouse.LeftButton == MouseButtonState.Pressed)
            return false;

        _hoverTimer.Stop();
        _closeTimer.Stop();
        _groupShortcutTimer?.Stop();

        var match = _groupShortcutSequence.Push(digit);
        switch (match.Kind)
        {
            case GroupShortcutMatchKind.Exact:
                OpenGroupByShortcut(match.Input);
                break;
            case GroupShortcutMatchKind.Partial:
            case GroupShortcutMatchKind.ExactWithLongerMatches:
                ShowGroupShortcutCandidates(match.Input);
                _groupShortcutTimer?.Start();
                break;
            case GroupShortcutMatchKind.NoMatch:
                ResetGroupShortcutInput();
                ShowToast($"No group shortcut {match.Input}");
                break;
        }
        return true;
    }

    private void OnGroupShortcutTimeout()
    {
        _groupShortcutTimer?.Stop();
        var match = _groupShortcutSequence.Timeout();
        if (match.Kind == GroupShortcutMatchKind.Exact)
        {
            OpenGroupByShortcut(match.Input);
            return;
        }

        ResetGroupShortcutInput();
        if (match.Input.Length > 0) ShowToast($"No group shortcut {match.Input}");
    }

    private void OpenGroupByShortcut(string code)
    {
        var group = TargetStore.Groups.FirstOrDefault(candidate => candidate.GroupCode == code);
        ResetGroupShortcutInput();
        if (group == null)
        {
            ShowToast($"No group shortcut {code}");
            return;
        }

        if (_open) EnterGroup(group);
        else
        {
            EnterGroup(group);
            OpenCloud();
        }
    }

    private void ShowGroupShortcutCandidates(string input)
    {
        if (!_open)
        {
            EnterGroup(null);
            OpenCloud();
        }
        else if (_currentGroup != null)
        {
            EnterGroup(null);
        }

        ShortcutIndicatorText.Text = input + "…";
        ShortcutIndicator.Visibility = Visibility.Visible;
        foreach (var element in Cloud.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is not TargetItem target) continue;
            element.Opacity = target.IsGroup
                && target.GroupCode?.StartsWith(input, StringComparison.Ordinal) == true
                ? 1.0
                : 0.25;
        }
    }

    private void ResetGroupShortcutInput(bool preserveActivation = true)
    {
        _groupShortcutTimer?.Stop();
        _groupShortcutSequence.Reset();
        HideGroupShortcutInput();
        _groupShortcutActivation.ResetInput(
            preserveActivation,
            wheelOpen: _open,
            hasCodes: TargetStore.Groups.Any(group => GroupShortcutSequence.IsValidCode(group.GroupCode)));
    }

    private void HideGroupShortcutInput()
    {
        ShortcutIndicator.Visibility = Visibility.Collapsed;
        foreach (var element in Cloud.Children.OfType<FrameworkElement>())
        {
            if (element.Tag is TargetItem target) element.Opacity = target.Exists ? 1.0 : 0.4;
        }
    }
}
