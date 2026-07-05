using System.Windows;
using System.Windows.Controls;
using Dropwheel.Models;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class TargetEditorWindow : Window
{
    private readonly TargetItem _target;
    private readonly TargetItem? _preselect;
    private readonly List<TargetItem?> _groupChoices = new() { null };

    public TargetEditorWindow(TargetItem t, TargetItem? preselectGroup = null)
    {
        InitializeComponent();
        _target = t;
        _preselect = preselectGroup;
        NameBox.Text = t.Name;
        PathBox.Text = t.Path;
        ActionBox.SelectedIndex = (int)t.Override;
        PinBox.IsChecked = t.Pinned;

        if (t.IsGroup)
        {
            // у группы нет пути/действия/родителя — прячем лишнее
            GroupLabel.Visibility = GroupCombo.Visibility = Visibility.Collapsed;
            PathBox.IsEnabled = false;
            ActionBox.IsEnabled = false;
        }
        else
        {
            GroupCombo.Items.Add("— (root)");
            foreach (var g in TargetStore.Groups)
            { _groupChoices.Add(g); GroupCombo.Items.Add(g.Name); }
            GroupCombo.SelectedIndex = Math.Max(0,
                _groupChoices.IndexOf(TargetStore.FindParentGroup(_target) ?? _preselect));
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        using var dlg = new WF.FolderBrowserDialog { SelectedPath = _target.Path };
        if (dlg.ShowDialog() == WF.DialogResult.OK) PathBox.Text = dlg.SelectedPath;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _target.Name = NameBox.Text.Trim();
        if (!_target.IsGroup)
        {
            _target.Path = PathBox.Text.Trim();
            _target.Override = (DropAction)ActionBox.SelectedIndex;
            TargetStore.MoveToGroup(_target, _groupChoices[Math.Max(0, GroupCombo.SelectedIndex)]);
        }
        _target.Pinned = PinBox.IsChecked == true;
        Close();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        TargetStore.RemoveEverywhere(_target);
        Close();
    }
}
