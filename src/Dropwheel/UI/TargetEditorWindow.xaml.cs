using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class TargetEditorWindow : Window
{
    private readonly TargetItem _target;
    private readonly TargetItem? _preselect;
    private readonly List<TargetItem?> _groupChoices = new() { null };
    private string? _tileColor;

    private static readonly string?[] TileColorChoices =
        { null, "#E23B3B", "#F59E0B", "#EAB308", "#22C55E", "#4C8BF5", "#A855F7", "#EC4899" };

    public TargetEditorWindow(TargetItem t, TargetItem? preselectGroup = null)
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        Shell.PrimaryClick += OnSave;
        Shell.DangerClick += OnDelete;
        _target = t;
        _preselect = preselectGroup;
        NameBox.Text = t.Name;
        EmojiBox.Text = t.Emoji ?? "";
        _tileColor = t.TileColor;
        BuildColorSwatches();
        GroupShortcutBox.Text = t.GroupCode ?? "";
        PathBox.Text = t.Path;
        ActionBox.SelectedIndex = (int)t.Override;
        NameTemplateBox.Text = t.NameTemplate ?? "";
        ConflictBox.SelectedIndex = (int)t.ConflictPolicy;
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
            GroupShortcutLabel.Visibility = GroupShortcutBox.Visibility = Visibility.Collapsed;
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

    /// <summary>Fills the tile-colour row with clickable swatches — one "no colour" chip plus a preset
    /// palette. Clicking a swatch selects it (highlighted) and remembers the hex; Save writes it to the
    /// target. Rebuilt on each click so the highlight follows the selection.</summary>
    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        foreach (var hex in TileColorChoices)
        {
            bool selected = string.Equals(hex ?? "", _tileColor ?? "", StringComparison.OrdinalIgnoreCase);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Background = hex == null
                    ? Brushes.Transparent
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush = selected ? Palettes.Accent : Palettes.Border,
                BorderThickness = new Thickness(selected ? 2.5 : 1),
                ToolTip = hex ?? "No colour (theme border)",
                Child = hex == null
                    ? new TextBlock
                    {
                        Text = "∅",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                    : null,
            };
            var captured = hex;
            swatch.MouseLeftButtonUp += (_, _) => { _tileColor = captured; BuildColorSwatches(); };
            ColorSwatches.Children.Add(swatch);
        }
    }

    /// <summary>Shows a validation problem inline above the footer instead of a pop-up, so the user
    /// stays in the form. Cleared on the next save attempt.</summary>
    private void ShowEditorError(string message)
    {
        EditorError.Text = message;
        EditorError.Visibility = Visibility.Visible;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        EditorError.Visibility = Visibility.Collapsed;
        if (_rulesMode && !TryValidateRules(out var error)) { ShowEditorError(error); return; }
        if (_target.IsGroup)
        {
            var code = GroupShortcutBox.Text.Trim();
            if (code.Length > 0 && !GroupShortcutSequence.IsValidCode(code))
            { ShowEditorError("Use one or two digits for the shortcut, or leave it empty."); return; }
            if (code.Length > 0 && TargetStore.Groups.Any(group =>
                    !ReferenceEquals(group, _target) && group.GroupCode == code))
            { ShowEditorError($"Shortcut {code} is already assigned to another group."); return; }
            _target.GroupCode = code.Length == 0 ? null : code;
        }
        _target.Name = NameBox.Text.Trim();
        var emoji = EmojiBox.Text.Trim();
        _target.Emoji = emoji.Length == 0 ? null : emoji;
        _target.TileColor = string.IsNullOrEmpty(_tileColor) ? null : _tileColor;
        if (!_target.IsGroup)
        {
            _target.Path = PathBox.Text.Trim();
            _target.Override = (DropAction)ActionBox.SelectedIndex;
            var nameTemplate = NameTemplateBox.Text.Trim();
            _target.NameTemplate = nameTemplate.Length == 0 ? null : nameTemplate;
            _target.ConflictPolicy = (ConflictPolicy)Math.Max(0, ConflictBox.SelectedIndex);
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

    /// <summary>Set when a plain target (or an empty group) was deleted, so the overlay can offer an
    /// Undo that re-inserts it where it was. Group deletions that dissolve or wipe children go through
    /// the GroupDeleteWindow confirmation instead and are not reported.</summary>
    public readonly record struct DeletedTarget(IList<TargetItem> List, TargetItem Item, int Index);

    public DeletedTarget? Deleted { get; private set; }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        // A group with children can lose a lot of configuration in one click, so confirm and offer a
        // non-destructive path (move the children out) instead of silently deleting everything.
        if (_target.IsGroup && _target.Children!.Count > 0)
        {
            var dlg = new GroupDeleteWindow(_target.Name, _target.Children.Count) { Owner = this };
            if (dlg.ShowDialog() != true) return; // cancelled — keep the group
            if (dlg.Choice == GroupDeleteChoice.KeepChildren)
            {
                TargetStore.DissolveGroup(_target);
                Close();
                return;
            }
            TargetStore.DeleteTarget(_target);
            Close();
            return;
        }
        // A plain target (or an empty group) deletes instantly; report it so the overlay can offer Undo.
        var parent = TargetStore.FindParentGroup(_target);
        IList<TargetItem> list = parent?.Children ?? TargetStore.Config.Targets;
        int index = Math.Max(0, list.IndexOf(_target));
        TargetStore.DeleteTarget(_target);
        Deleted = new DeletedTarget(list, _target, index);
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
            ShowEditorError("Custom launch needs a program.");
            return false;
        }
        _target.Launch = options;
        return true;
    }
}
