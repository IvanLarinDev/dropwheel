using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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
        Visibility = Visibility.Visible;
        Restart(kind == ToastKind.Danger || undo != null ? 8 : 4);
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
        var undo = _undo;
        Hide();
        undo?.Invoke();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => _timer.Stop();

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (Visibility == Visibility.Visible) Restart(4);
    }
}
