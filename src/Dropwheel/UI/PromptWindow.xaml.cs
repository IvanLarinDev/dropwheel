using System.Windows;

namespace Dropwheel.UI;

public partial class PromptWindow : Window
{
    public string Value => Input.Text;

    public PromptWindow(string title, string caption)
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        Title = title;
        Shell.Title = title;
        Caption.Text = caption;
        Shell.PrimaryClick += (_, _) => DialogResult = true;
        Shell.IsPrimaryEnabled = false;
        Input.TextChanged += (_, _) =>
        {
            bool ok = Input.Text.Trim().Length > 0;
            Shell.IsPrimaryEnabled = ok;
            Hint.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        };
        Loaded += (_, _) => Input.Focus();
    }
}
