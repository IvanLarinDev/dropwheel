using System.Windows;
using System.Windows.Controls;

namespace Dropwheel.UI;

/// <summary>Shared dialog frame used by every window: a bold title with an optional subtitle, the
/// caller's content, and a footer that always follows the same contract — the Primary verb on the
/// right (bound to Enter), Cancel directly to its left (bound to Esc), and an optional destructive
/// action as a quiet danger-text button far left. Windows put their content inside it so all dialogs
/// read the same. Cancel and Esc close the owning dialog with DialogResult=false; Primary raises
/// <see cref="PrimaryClick"/> so the window can validate before closing.</summary>
[TemplatePart(Name = "PART_Primary", Type = typeof(Button))]
[TemplatePart(Name = "PART_Danger", Type = typeof(Button))]
public class DialogShell : ContentControl
{
    static DialogShell()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DialogShell), new FrameworkPropertyMetadata(typeof(DialogShell)));
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(DialogShell));

    /// <summary>Header line. Empty or null hides the whole header (title + subtitle).</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(DialogShell));

    /// <summary>One-line explanation under the title. Empty or null hides just the subtitle.</summary>
    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty PrimaryTextProperty =
        DependencyProperty.Register(nameof(PrimaryText), typeof(string), typeof(DialogShell),
            new PropertyMetadata("OK"));

    /// <summary>The Primary button label — always a verb (Save, Create, Delete group).</summary>
    public string PrimaryText
    {
        get => (string)GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public static readonly DependencyProperty DangerTextProperty =
        DependencyProperty.Register(nameof(DangerText), typeof(string), typeof(DialogShell));

    /// <summary>Label for the far-left destructive action. Null or empty hides it.</summary>
    public string? DangerText
    {
        get => (string?)GetValue(DangerTextProperty);
        set => SetValue(DangerTextProperty, value);
    }

    public static readonly DependencyProperty IsPrimaryEnabledProperty =
        DependencyProperty.Register(nameof(IsPrimaryEnabled), typeof(bool), typeof(DialogShell),
            new PropertyMetadata(true));

    /// <summary>Whether the Primary button is clickable. Windows turn it off while the form is
    /// invalid (e.g. an empty required name) so the only path forward is to fix the input.</summary>
    public bool IsPrimaryEnabled
    {
        get => (bool)GetValue(IsPrimaryEnabledProperty);
        set => SetValue(IsPrimaryEnabledProperty, value);
    }

    public static readonly DependencyProperty ShowCancelProperty =
        DependencyProperty.Register(nameof(ShowCancel), typeof(bool), typeof(DialogShell),
            new PropertyMetadata(true));

    /// <summary>Whether the Cancel button is shown. Off for single-button message boxes.</summary>
    public bool ShowCancel
    {
        get => (bool)GetValue(ShowCancelProperty);
        set => SetValue(ShowCancelProperty, value);
    }

    /// <summary>Raised when the user activates the Primary button (or presses Enter). The window
    /// validates and, if all is well, sets DialogResult=true and closes.</summary>
    public event RoutedEventHandler? PrimaryClick;

    /// <summary>Raised when the user activates the destructive action.</summary>
    public event RoutedEventHandler? DangerClick;

    public DialogShell()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Primary") is Button primary)
            primary.Click += (_, e) => PrimaryClick?.Invoke(this, e);
        if (GetTemplateChild("PART_Danger") is Button danger)
            danger.Click += (_, e) => DangerClick?.Invoke(this, e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Themes.ThemeChanged += Repaint;

    private void OnUnloaded(object sender, RoutedEventArgs e) => Themes.ThemeChanged -= Repaint;

    /// <summary>Re-applies the palette to the owning window so an open dialog follows a live theme
    /// change; the footer/header brushes are DynamicResource and refresh with it.</summary>
    private void Repaint()
    {
        if (Window.GetWindow(this) is { } w) Themes.ApplyWindow(w);
    }
}
