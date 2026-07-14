using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

/// <summary>The outcome a toast reports, which picks its dot color from the palette roles.</summary>
public enum ToastKind { Info, Success, Warning, Danger }

/// <summary>A single self-timing toast that any window can host: it paints itself from the theme
/// (dark backdrop so white text reads over any window or the desktop), shows one message at a time,
/// and offers an optional Undo link. Hovering freezes the auto-close timer so a slow reader (or a
/// hand reaching for Undo) doesn't lose it.</summary>
public partial class ToastHost : UserControl
{
    private readonly DispatcherTimer _timer;
    private Action? _undo;
    private int _currentSeconds = 4;

    /// <summary>Seconds this toast should stay up, from the configured base.</summary>
    private static int ToastSeconds(ToastKind kind, bool undoable) =>
        ToastSecondsFor(TargetStore.Config.ToastSeconds, kind, undoable);

    /// <summary>The base seconds (clamped to 1–60) doubled for a Danger or undoable toast so it isn't
    /// missed. Pure, so the timing rule can be tested without the global config.</summary>
    internal static int ToastSecondsFor(int baseSeconds, ToastKind kind, bool undoable)
    {
        int clamped = Math.Clamp(baseSeconds, 1, 60);
        return kind == ToastKind.Danger || undoable ? clamped * 2 : clamped;
    }

    public ToastHost()
    {
        InitializeComponent();
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Hide();
    }

    /// <summary>Shows a message, replacing any current one. Pass an undo action to reveal the Undo
    /// link. Danger and undoable toasts linger longer so they aren't missed.</summary>
    public void Show(string message, ToastKind kind = ToastKind.Info, Action? undo = null)
    {
        MessageText.Text = message;
        _undo = undo;
        UndoLink.Visibility = undo != null ? Visibility.Visible : Visibility.Collapsed;
        KindDot.Fill = kind switch
        {
            ToastKind.Success => Palettes.Success,
            ToastKind.Warning => Palettes.Warning,
            ToastKind.Danger => Palettes.Danger,
            _ => Palettes.Info,
        };
        Root.Background = new SolidColorBrush(Themes.Current.Backdrop);
        UndoLink.Foreground = new SolidColorBrush(Themes.Current.Accent);
        AutomationProperties.SetLiveSetting(this,
            kind == ToastKind.Danger ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        AutomationProperties.SetName(this, undo != null ? $"{message}. Undo available." : message);
        Visibility = Visibility.Visible;
        var peer = UIElementAutomationPeer.FromElement(this)
                   ?? UIElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        PlaySound(kind);
        _currentSeconds = ToastSeconds(kind, undo != null);
        Restart(_currentSeconds);
    }

    /// <summary>Plays a short system sound for a Warning or Danger toast when the option is on. Info and
    /// Success stay silent so routine drops don't chirp.</summary>
    private static void PlaySound(ToastKind kind)
    {
        if (!TargetStore.Config.ToastSound) return;
        switch (kind)
        {
            case ToastKind.Danger: SystemSounds.Hand.Play(); break;
            case ToastKind.Warning: SystemSounds.Exclamation.Play(); break;
        }
    }

    /// <summary>Hides the toast and forgets any pending undo.</summary>
    public void Hide()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
        _undo = null;
    }

    private void Restart(int seconds)
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
    }

    private void OnUndo(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        InvokeUndo();
    }

    private void OnUndoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        InvokeUndo();
    }

    private void InvokeUndo()
    {
        var undo = _undo;
        Hide();
        undo?.Invoke();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => _timer.Stop();

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        // Restart with this toast's own duration, not a hard 4s, so a long (Danger/undo) toast keeps it.
        if (Visibility == Visibility.Visible) Restart(_currentSeconds);
    }
}
