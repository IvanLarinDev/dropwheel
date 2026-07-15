using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Dropwheel.Services;

namespace Dropwheel.UI;

/// <summary>Read-only reference for Destination tokens, opened from the token chips in the rule editor.
/// The left list picks a category; the right pane shows that category's tokens with an example of what
/// each expands to, or the format and limits notes. The token rows come from SortService.TokenDocs so
/// the help never drifts from the engine.</summary>
public partial class TokenHelpWindow : Window
{
    private static readonly FontFamily Mono = new("Consolas");

    private static readonly string[] Categories =
        { "Drop date", "File date", "File name", "Size", "Formats", "Limits" };

    public TokenHelpWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        Shell.PrimaryClick += (_, _) => { DialogResult = true; };
        foreach (var c in Categories) CategoryList.Items.Add(c);
        CategoryList.SelectedIndex = 0;
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailHost.Children.Clear();
        switch (CategoryList.SelectedIndex)
        {
            case 0: BuildTokens(SortService.TokenGroup.DropDate); break;
            case 1: BuildTokens(SortService.TokenGroup.FileDate); break;
            case 2: BuildTokens(SortService.TokenGroup.FileName); break;
            case 3: BuildTokens(SortService.TokenGroup.Size); break;
            case 4: BuildFormats(); break;
            default: BuildLimits(); break;
        }
    }

    private void BuildTokens(SortService.TokenGroup group)
    {
        if (group == SortService.TokenGroup.FileDate)
            DetailHost.Children.Add(Note("The same components as the drop-date tokens, but read from the "
                + "file itself: an f- prefix uses its modified time, a c- prefix its created time."));
        foreach (var d in SortService.TokenDocs())
            if (d.Group == group) DetailHost.Children.Add(TokenRow(d));
    }

    private void BuildFormats()
    {
        DetailHost.Children.Add(Note("Add a format after a colon inside the braces."));
        DetailHost.Children.Add(SubHead("Dates"));
        DetailHost.Children.Add(ExampleRow("${date:dd-MM-yy}", "14-03-26"));
        DetailHost.Children.Add(ExampleRow("${month:MMMM}", "March"));
        DetailHost.Children.Add(Note("Any .NET date format works on a date token."));
        DetailHost.Children.Add(SubHead("Size buckets"));
        DetailHost.Children.Add(ExampleRow("${size: tiny 0.5, small 10, huge}", "small"));
        DetailHost.Children.Add(Note("Comma-separated \"name limit\" buckets, the limit in MB and ascending; "
            + "the last bucket may drop its limit as a catch-all."));
        DetailHost.Children.Add(SubHead("Length and padding"));
        DetailHost.Children.Add(ExampleRow("${stem:8}", "Holiday "));
        DetailHost.Children.Add(Note("A number after ${stem} caps the name to that many characters."));
        DetailHost.Children.Add(ExampleRow("${ep:3}", "007"));
        DetailHost.Children.Add(Note("A number after a numeric regex-group pads it with leading zeros."));
        DetailHost.Children.Add(SubHead("Several tokens"));
        DetailHost.Children.Add(ExampleRow("${year}\\${month}\\${ext}", "2026\\03\\jpg"));
        DetailHost.Children.Add(Note("Combine as many tokens as you like in one path."));
    }

    private void BuildLimits()
    {
        DetailHost.Children.Add(Bullet("Token names are latin letters and digits only — no underscores, "
            + "spaces, or Cyrillic."));
        DetailHost.Children.Add(Bullet("An unknown or empty token sends the file to the target root instead "
            + "of a half-built path."));
        DetailHost.Children.Add(Bullet("The f-/c- date tokens and ${size} need the file on disk; for text "
            + "and virtual drops they can be empty."));
        DetailHost.Children.Add(Bullet("A built-in token name shadows a Name-regex group with the same name."));
    }

    /// <summary>A token entry: the placeholder and its example on one line, the summary muted below, and
    /// a ":format" marker when the token accepts one.</summary>
    private FrameworkElement TokenRow(SortService.TokenDoc d)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var head = new TextBlock { Margin = new Thickness(0, 0, 0, 1) };
        head.Inlines.Add(new Run("${" + d.Name + "}") { FontFamily = Mono, Foreground = Palettes.Text });
        head.Inlines.Add(new Run("   →   " + d.Example) { Foreground = Palettes.Accent });
        if (d.TakesFormat)
            head.Inlines.Add(new Run("   :format") { FontFamily = Mono, FontSize = 10, Foreground = Palettes.TextMuted });
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = d.Summary,
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private FrameworkElement ExampleRow(string token, string result)
    {
        var head = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        head.Inlines.Add(new Run(token) { FontFamily = Mono, Foreground = Palettes.Text });
        head.Inlines.Add(new Run("   →   " + result) { FontFamily = Mono, Foreground = Palettes.Accent });
        return head;
    }

    private FrameworkElement SubHead(string text) => new TextBlock
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 6, 0, 6),
    };

    private FrameworkElement Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Foreground = Palettes.TextMuted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private FrameworkElement Bullet(string text)
    {
        var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        row.Inlines.Add(new Run("•  ") { Foreground = Palettes.Accent });
        row.Inlines.Add(new Run(text));
        return row;
    }
}
