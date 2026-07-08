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
        LoadLaunchOptions(t.Launch);

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
                _rules.AddRange(_target.Rules.Select(r => r.Clone()));
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
            if (!TrySaveLaunchOptions()) return;
            TargetStore.MoveToGroup(_target, _groupChoices[Math.Max(0, GroupCombo.SelectedIndex)]);
            if (_rulesMode)
            {
                _target.Rules = _rules.Count > 0 ? _rules.Select(r => r.Clone()).ToList() : null;
                _target.SortRules = null;
                _target.Watch = WatchBox.IsChecked == true && _target.Rules != null;
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

    private void LoadLaunchOptions(LaunchOptions? options)
    {
        LaunchModeBox.SelectedIndex = options == null ? 0 : 1;
        LaunchProgramBox.Text = options?.FileName ?? "{target}";
        LaunchArgsBox.Text = options?.Arguments ?? DefaultLaunchArguments(PathBox.Text);
        LaunchWorkDirBox.Text = options?.WorkingDirectory ?? "{targetDir}";
        RefreshLaunchOptions();
    }

    private void OnLaunchModeChanged(object sender, SelectionChangedEventArgs e) => RefreshLaunchOptions();

    private void OnLaunchEditChanged(object sender, TextChangedEventArgs e) => RefreshLaunchOptions();

    private void RefreshLaunchOptions()
    {
        if (!IsLaunchPath(PathBox.Text))
        {
            LaunchPanel.Visibility = Visibility.Collapsed;
            return;
        }

        LaunchPanel.Visibility = Visibility.Visible;
        var custom = LaunchModeBox.SelectedIndex == 1;
        LaunchProgramBox.IsEnabled = LaunchArgsBox.IsEnabled = LaunchWorkDirBox.IsEnabled = custom;
        var path = string.IsNullOrWhiteSpace(PathBox.Text) ? @"C:\Tools\target.bat" : PathBox.Text.Trim();
        var options = custom ? CurrentLaunchOptions() : null;
        var psi = LaunchService.BuildStartInfo(path, new[] { @"C:\Drop\a.txt", @"C:\Drop\b b.txt" }, options);
        LaunchPreviewText.Text = string.IsNullOrWhiteSpace(psi.Arguments)
            ? psi.FileName
            : $"{psi.FileName} {psi.Arguments}";
        LaunchStatusText.Text = custom
            ? "Custom launch applies only to this target."
            : "Default launch: Dropwheel runs this target with dropped files.";
    }

    private static bool IsLaunchPath(string path) => TargetItem.IsExeExtension(path);

    private static string DefaultLaunchArguments(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => "-NoProfile -ExecutionPolicy Bypass -File \"{target}\" {files}",
            ".py" or ".pyw" => "\"{target}\" {files}",
            ".jar" => "-jar \"{target}\" {files}",
            _ => "{files}",
        };
    }

    private LaunchOptions CurrentLaunchOptions() => new()
    {
        FileName = LaunchProgramBox.Text.Trim(),
        Arguments = LaunchArgsBox.Text.Trim(),
        WorkingDirectory = LaunchWorkDirBox.Text.Trim(),
    };

    private bool TrySaveLaunchOptions()
    {
        if (!IsLaunchPath(_target.Path))
        {
            _target.Launch = null;
            return true;
        }
        if (LaunchModeBox.SelectedIndex != 1)
        {
            _target.Launch = null;
            return true;
        }

        var options = CurrentLaunchOptions();
        if (string.IsNullOrWhiteSpace(options.FileName))
        {
            MessageBox.Show(this, "Program is required for custom launch.",
                "Launch options", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _target.Launch = options;
        return true;
    }
}
