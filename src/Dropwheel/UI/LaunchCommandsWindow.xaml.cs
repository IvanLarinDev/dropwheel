using System.Windows;
using System.Windows.Controls;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class LaunchCommandsWindow : Window
{
    private readonly List<LaunchCommand> _commands;
    private bool _loading;

    public LaunchCommandsWindow()
    {
        InitializeComponent();
        Themes.ApplyWindow(this);
        ApplyPalette();
        _commands = CloneCommands(TargetStore.Config.LaunchCommands ?? TargetStore.DefaultLaunchCommands());
        RebuildList(0);
    }

    private void ApplyPalette()
    {
        DetailPanel.Background = Palettes.Surface;
        DetailPanel.BorderBrush = Palettes.Border;
        PreviewPanel.Background = Palettes.Brush(Palettes.Alpha(Palettes.Current.Surface, 0xCC));
        PreviewPanel.BorderBrush = Palettes.Border;
        StatusText.Foreground = Palettes.TextMuted;
    }

    private static List<LaunchCommand> CloneCommands(IEnumerable<LaunchCommand> commands) =>
        commands.Select(c => new LaunchCommand
        {
            Extensions = c.Extensions.ToList(),
            FileName = c.FileName,
            Arguments = c.Arguments,
        }).ToList();

    private void RebuildList(int selected)
    {
        CommandList.Items.Clear();
        foreach (var command in _commands)
            CommandList.Items.Add(Summary(command));
        if (_commands.Count == 0)
        {
            LoadCommand(null);
            return;
        }
        CommandList.SelectedIndex = Math.Clamp(selected, 0, _commands.Count - 1);
    }

    private static string Summary(LaunchCommand command)
    {
        var exts = command.Extensions.Count == 0
            ? "(no extension)"
            : string.Join(", ", command.Extensions);
        var file = string.IsNullOrWhiteSpace(command.FileName) ? "(no program)" : command.FileName;
        return $"{exts}  -  {file}";
    }

    private LaunchCommand? Selected =>
        CommandList.SelectedIndex >= 0 && CommandList.SelectedIndex < _commands.Count
            ? _commands[CommandList.SelectedIndex]
            : null;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => LoadCommand(Selected);

    private void LoadCommand(LaunchCommand? command)
    {
        _loading = true;
        var enabled = command != null;
        ExtensionsBox.IsEnabled = ProgramBox.IsEnabled = ArgumentsBox.IsEnabled = enabled;
        ExtensionsBox.Text = command == null ? "" : string.Join(" ", command.Extensions);
        ProgramBox.Text = command?.FileName ?? "";
        ArgumentsBox.Text = command?.Arguments ?? "";
        _loading = false;
        RefreshPreview();
    }

    private void OnEditChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected == null) return;
        Selected.Extensions = ParseExtensions(ExtensionsBox.Text);
        Selected.FileName = ProgramBox.Text.Trim();
        Selected.Arguments = ArgumentsBox.Text.Trim();
        var index = CommandList.SelectedIndex;
        _loading = true;
        RebuildList(index);
        _loading = false;
        RefreshPreview();
    }

    private static List<string> ParseExtensions(string text) =>
        text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeExtension)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string NormalizeExtension(string extension)
    {
        var ext = extension.Trim();
        if (ext.Length == 0) return "";
        return ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
    }

    private void RefreshPreview()
    {
        if (Selected == null)
        {
            PreviewText.Text = "Add a command to see its preview.";
            StatusText.Text = "";
            return;
        }

        var target = Selected.Extensions.FirstOrDefault() is { Length: > 0 } ext
            ? @"C:\Tools\sample" + ext
            : @"C:\Tools\sample.ext";
        var psi = LaunchService.BuildStartInfo(target, new[] { @"C:\Drop\a.txt", @"C:\Drop\b b.txt" }, new[] { Selected });
        PreviewText.Text = string.IsNullOrWhiteSpace(psi.Arguments)
            ? psi.FileName
            : $"{psi.FileName} {psi.Arguments}";
        StatusText.Text = Validate(Selected, out var isWarning);
        StatusText.Foreground = isWarning ? Palettes.Brush(System.Windows.Media.Colors.DarkOrange) : Palettes.TextMuted;
    }

    private static string Validate(LaunchCommand command, out bool isWarning)
    {
        isWarning = true;
        if (command.Extensions.Count == 0) return "Add at least one extension.";
        if (string.IsNullOrWhiteSpace(command.FileName)) return "Choose a program or command name.";
        if (string.IsNullOrWhiteSpace(command.Arguments)) return "Arguments are empty; the target and dropped files will not be passed.";
        if (!command.Arguments.Contains("{target}", StringComparison.Ordinal)) return "Arguments should include {target}.";
        if (!command.Arguments.Contains("{files}", StringComparison.Ordinal))
            return "Arguments do not include {files}; dropped files will not be passed.";
        isWarning = false;
        return "Ready. Preview shows the exact command shape for a sample drop.";
    }

    private void OnPreview(object sender, RoutedEventArgs e) => RefreshPreview();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        _commands.Add(new LaunchCommand { Extensions = { ".ext" }, FileName = "program.exe", Arguments = "\"{target}\" {files}" });
        RebuildList(_commands.Count - 1);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var index = CommandList.SelectedIndex;
        if (index < 0) return;
        _commands.RemoveAt(index);
        RebuildList(Math.Min(index, _commands.Count - 1));
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        _commands.Clear();
        _commands.AddRange(CloneCommands(TargetStore.DefaultLaunchCommands()));
        RebuildList(0);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        foreach (var command in _commands)
        {
            var message = Validate(command, out _);
            if (message.StartsWith("Add ", StringComparison.Ordinal)
                || message.StartsWith("Choose ", StringComparison.Ordinal))
            {
                MessageBox.Show(this, message, "Launch commands", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        TargetStore.Config.LaunchCommands = CloneCommands(_commands);
        TargetStore.Save();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
