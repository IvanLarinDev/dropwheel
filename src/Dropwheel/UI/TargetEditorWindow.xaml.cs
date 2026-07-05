using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class TargetEditorWindow : Window
{
    private readonly TargetItem _target;

    public TargetEditorWindow(TargetItem t)
    {
        InitializeComponent();
        _target = t;
        NameBox.Text = t.Name;
        PathBox.Text = t.Path;
        ActionBox.SelectedIndex = (int)t.Override;
        PinBox.IsChecked = t.Pinned;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        using var dlg = new WF.FolderBrowserDialog { SelectedPath = _target.Path };
        if (dlg.ShowDialog() == WF.DialogResult.OK) PathBox.Text = dlg.SelectedPath;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _target.Name = NameBox.Text.Trim();
        _target.Path = PathBox.Text.Trim();
        _target.Override = (DropAction)ActionBox.SelectedIndex;
        _target.Pinned = PinBox.IsChecked == true;
        if (!TargetStore.Config.Targets.Contains(_target))
            TargetStore.Config.Targets.Add(_target);
        Close();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        TargetStore.Config.Targets.Remove(_target);
        Close();
    }
}
