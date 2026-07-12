using System.Windows;
using System.Windows.Controls;

namespace Dropwheel.UI;

/// <summary>A themed replacement for the stock MessageBox: a <see cref="DialogShell"/>-framed window
/// that matches the app's palette. Falls back to the system MessageBox if the themed window can't be
/// built (e.g. a failure early in startup, before the config and palette are loaded), so an early
/// error can still reach the user.</summary>
public static class DwMessageBox
{
    /// <summary>Shows a message and returns true if the user pressed the primary button, false on
    /// Cancel or Esc. Pass showCancel to make it a two-button confirm; leave it off for a plain notice.</summary>
    public static bool Show(Window? owner, string caption, string message,
        string primaryText = "OK", bool showCancel = false)
    {
        try
        {
            return ShowThemed(owner, caption, message, primaryText, showCancel);
        }
        catch
        {
            var buttons = showCancel ? MessageBoxButton.OKCancel : MessageBoxButton.OK;
            var result = owner != null
                ? MessageBox.Show(owner, message, caption, buttons)
                : MessageBox.Show(message, caption, buttons);
            return result == MessageBoxResult.OK;
        }
    }

    private static bool ShowThemed(Window? owner, string caption, string message,
        string primaryText, bool showCancel)
    {
        var shell = new DialogShell
        {
            Title = caption,
            PrimaryText = primaryText,
            ShowCancel = showCancel,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 4),
            },
        };
        var window = new Window
        {
            Title = "Dropwheel",
            Content = shell,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation =
                owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = false,
            Owner = owner,
        };
        Themes.ApplyWindow(window);
        shell.PrimaryClick += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }
}
