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
        Themes.ApplyWindow(this);
        _target = t;
        _preselect = preselectGroup;
        NameBox.Text = t.Name;
        PathBox.Text = t.Path;
        ActionBox.SelectedIndex = (int)t.Override;
        PinBox.IsChecked = t.Pinned;

        if (t.IsGroup)
        {
            // a group has no path/action/parent — hide the irrelevant fields
            GroupLabel.Visibility = GroupCombo.Visibility = Visibility.Collapsed;
            PathBox.IsEnabled = false;
            ActionBox.IsEnabled = false;
            ConvertBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            GroupCombo.Items.Add("— (root)");
            foreach (var g in TargetStore.Groups)
            { _groupChoices.Add(g); GroupCombo.Items.Add(g.Name); }
            GroupCombo.SelectedIndex = Math.Max(0,
                _groupChoices.IndexOf(TargetStore.FindParentGroup(_target) ?? _preselect));

            SortMigration.Migrate(_target);
            if (_target.Rules is { Count: > 0 })
            {
                _rules.AddRange(_target.Rules);
                ShowRulesEditor();
            }
            else
            {
                // Routing rules distribute files into subfolders, so they only apply to a
                // folder target — not to an executable, a file, or a missing path.
                ConvertBtn.Visibility = _target.IsFolder ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        using var dlg = new WF.FolderBrowserDialog { SelectedPath = _target.Path };
        if (dlg.ShowDialog() == WF.DialogResult.OK) PathBox.Text = dlg.SelectedPath;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_rulesMode && !TryValidateRules(out var error))
        {
            MessageBox.Show(this, error, "Rules", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _target.Name = NameBox.Text.Trim();
        if (!_target.IsGroup)
        {
            _target.Path = PathBox.Text.Trim();
            _target.Override = (DropAction)ActionBox.SelectedIndex;
            TargetStore.MoveToGroup(_target, _groupChoices[Math.Max(0, GroupCombo.SelectedIndex)]);
            if (_rulesMode)
            {
                _target.Rules = _rules.Count > 0 ? _rules : null;
                _target.SortRules = null;
            }
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
