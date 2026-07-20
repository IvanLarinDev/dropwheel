using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class TargetEditorWindow
{
    private void OnPreviewChanged(object sender, TextChangedEventArgs e) => RefreshMatches();

    private void OnPreviewFilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPreviewFilesDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var addition = string.Join(Environment.NewLine, paths);
        var existing = PreviewInput.Text;
        PreviewInput.Text = string.IsNullOrWhiteSpace(existing)
            ? addition
            : existing.TrimEnd('\r', '\n') + Environment.NewLine + addition;
        PreviewInput.CaretIndex = PreviewInput.Text.Length;
        e.Handled = true;
    }

    private void RefreshMatches()
    {
        if (_matchesHost == null) return;
        _matchesHost.Children.Clear();
        var files = (PreviewInput.Text ?? "")
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).Where(value => value.Length > 0).ToArray();
        if (files.Length == 0)
        {
            _matchesHost.Children.Add(Hint("Paste file names into Test files to preview routing."));
            return;
        }
        if (_selected < 0 || _selected >= _rules.Count) return;

        var firstCatchAll = -1;
        for (var i = 0; i < _rules.Count; i++)
            if (_rules[i].All.Count == 0 && SortService.ScopeIncludes(_rules[i].Scope, isDirectory: false))
            { firstCatchAll = i; break; }
        if (firstCatchAll >= 0 && _selected > firstCatchAll)
            _matchesHost.Children.Add(Hint(
                $"Unreachable: rule {firstCatchAll + 1} ({FriendlyDest(_rules[firstCatchAll].Dest)}) " +
                "is a catch-all and takes every file first. Move it down or add conditions."));

        var here = files.Where(file => SortService.MatchedRuleIndex(_rules, file) == _selected).ToArray();
        _matchesHost.Children.Add(new TextBlock
        {
            Text = $"Routes here: {here.Length} of {files.Length}",
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            Margin = new Thickness(0, 0, 0, 2),
        });
        var showDestination = _rules[_selected].Dest.Contains("${", StringComparison.Ordinal);
        foreach (var file in here)
            _matchesHost.Children.Add(new TextBlock
            {
                Text = showDestination
                    ? $"{Path.GetFileName(file)}  →  {ResolvedDestLabel(_rules[_selected], file)}"
                    : Path.GetFileName(file),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        if (_rules.Any(rule => rule.All.Any(condition =>
                condition.Field is ConditionField.SizeMb or ConditionField.AgeDays)))
            _matchesHost.Children.Add(Hint(
                "Size/age here are treated as 0 — test names are not real files, so these preview only by extension/name."));
        if (SortService.ParseTokens(_rules[_selected].Dest)
                .Any(token => (token.Name.StartsWith('f') || token.Name.StartsWith('c'))
                    && SortService.BuiltinTokens.Contains(token.Name)))
            _matchesHost.Children.Add(Hint(
                "File-date tokens (${fdate}… / ${cdate}…) resolve from the real file at drop time; a test name with no real file routes to the root here."));
    }

    private static string ResolvedDestLabel(SortRule rule, string filePath)
    {
        var expanded = SortService.ExpandTemplate(rule, filePath, out var ok);
        return ok ? expanded : "(root — unfilled tokens)";
    }
}
