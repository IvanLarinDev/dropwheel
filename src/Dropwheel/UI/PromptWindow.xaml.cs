using System.Windows;

namespace Dropwheel.UI;

public partial class PromptWindow : Window
{
    public string Value => Input.Text;

    public PromptWindow(string title, string caption)
    {
        InitializeComponent();
        Title = title;
        Caption.Text = caption;
        Loaded += (_, _) => Input.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
