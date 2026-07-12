using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class TargetEditorWindow
{
    private readonly List<SortRule> _rules = new();
    private int _selected = -1;
    private bool _rulesMode;
    private StackPanel? _matchesHost;
    private TextBlock? _tokenHint;

    private static Brush SelectedBg => Palettes.Selection;
    private static Brush SelectedBar => Palettes.Accent;

    /// <summary>Operators offered for each field. Fields with a single operator show it as a
    /// static word instead of a dropdown.</summary>
    private static readonly Dictionary<ConditionField, CompareOp[]> OpsFor = new()
    {
        [ConditionField.Extension] = new[] { CompareOp.In },
        [ConditionField.NameContains] = new[] { CompareOp.Contains },
        [ConditionField.NameRegex] = new[] { CompareOp.Matches },
        [ConditionField.SizeMb] = new[] { CompareOp.Gt, CompareOp.Lt, CompareOp.Gte, CompareOp.Lte },
        [ConditionField.AgeDays] = new[] { CompareOp.Gt, CompareOp.Lt, CompareOp.Gte, CompareOp.Lte },
    };

    private void ShowRulesEditor()
    {
        _rulesMode = true;
        MasterPanel.Visibility = Visibility.Visible;
        DetailPanel.Visibility = Visibility.Visible;
        DetailPanel.Background = Palettes.Surface;
        DetailPanel.BorderBrush = Palettes.Accent;
        PreviewPanel.Visibility = Visibility.Visible;
        PreviewPanel.Background = Palettes.Surface;
        PreviewPanel.BorderBrush = Palettes.Accent;
        _matchesHost = MatchesHost;
        WatchBox.IsChecked = _target.Watch;
        ConvertBtn.Visibility = Visibility.Collapsed;
        if (_selected < 0 && _rules.Count > 0) _selected = 0;
        RebuildMaster();
        RebuildDetail();
    }

    private void OnConvertToRules(object sender, RoutedEventArgs e)
    {
        if (_rules.Count == 0) _rules.Add(new SortRule());
        _selected = 0;
        ShowRulesEditor();
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        _rules.Add(new SortRule());
        _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
    }

    // ── Master list ────────────────────────────────────────────────────────

    private void RebuildMaster()
    {
        MasterHost.Children.Clear();
        for (int i = 0; i < _rules.Count; i++)
            MasterHost.Children.Add(BuildMasterRow(_rules[i], i));
    }

    private FrameworkElement BuildMasterRow(SortRule rule, int index)
    {
        bool sel = index == _selected;
        var name = new TextBlock
        {
            Text = $"{index + 1}.  {FriendlyDest(rule.Dest)}",
            FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var summary = new TextBlock
        {
            Text = MasterSummary(rule),
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var body = new StackPanel { Margin = new Thickness(8, 4, 6, 4) };
        body.Children.Add(name);
        body.Children.Add(summary);

        var border = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = sel ? SelectedBg : Brushes.Transparent,
            BorderBrush = sel ? SelectedBar : Brushes.Transparent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            ToolTip = string.IsNullOrWhiteSpace(rule.Dest)
                ? "→ (target root)" : $"→ {rule.Dest}",
        };
        border.MouseLeftButtonUp += (_, _) => { _selected = index; RebuildMaster(); RebuildDetail(); };
        return border;
    }

    private static string MasterSummary(SortRule rule)
    {
        if (rule.All.Count == 0) return "catch-all";
        return string.Join(", ", rule.All.Select(c => $"{FieldWord(c.Field)} {ShortValue(c)}"));
    }

    private static string ShortValue(RuleCondition c) => c.Value.Length > 18 ? c.Value[..18] + "…" : c.Value;

    /// <summary>Shows the destination as its leaf folder name so long absolute paths stay readable
    /// in the narrow list. The full path is available via the row tooltip.</summary>
    private static string FriendlyDest(string dest)
    {
        if (string.IsNullOrWhiteSpace(dest)) return "(root)";
        var trimmed = dest.TrimEnd('\\', '/');
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? trimmed : leaf;
    }

    // ── Detail editor ──────────────────────────────────────────────────────

    private void RebuildDetail()
    {
        DetailHost.Children.Clear();
        if (_selected < 0 || _selected >= _rules.Count)
        {
            DetailHost.Children.Add(Hint("Select or add a rule."));
            RefreshMatches();
            return;
        }
        var rule = _rules[_selected];

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(tools, Dock.Right);
        tools.Children.Add(MoveButton("▲", "Move rule up", _selected > 0, () => MoveSelected(-1)));
        tools.Children.Add(MoveButton("▼", "Move rule down", _selected < _rules.Count - 1, () => MoveSelected(+1)));
        tools.Children.Add(MoveButton("✕", "Delete rule", true, DeleteSelected));
        header.Children.Add(tools);
        header.Children.Add(new TextBlock { Text = $"Rule {_selected + 1}", FontWeight = FontWeights.SemiBold });
        DetailHost.Children.Add(header);

        DetailHost.Children.Add(new TextBlock { Text = "Destination (subfolder or absolute)", FontSize = 11 });
        var destRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var browse = new Button { Content = "…", Width = 26, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) => BrowseDest(rule);
        var destBox = new TextBox
        {
            Text = rule.Dest,
            ToolTip = "A subfolder (relative to the target Path) or an absolute path. "
                    + "Use ${name} to insert a (?<name>…) group captured by a Name regex condition, "
                    + "e.g. episodes\\${ep}\\${sq}\\${sh}.",
        };
        destBox.TextChanged += (_, _) => { rule.Dest = destBox.Text; RebuildMaster(); RebuildTokenHint(rule); RefreshMatches(); };
        destRow.Children.Add(browse);
        destRow.Children.Add(destBox);
        DetailHost.Children.Add(destRow);

        _tokenHint = new TextBlock
        {
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DetailHost.Children.Add(_tokenHint);
        RebuildTokenHint(rule);

        DetailHost.Children.Add(new TextBlock { Text = "Conditions (all must match)", FontSize = 11 });
        foreach (var c in rule.All)
            DetailHost.Children.Add(BuildConditionRow(rule, c));
        if (rule.All.Count == 0)
            DetailHost.Children.Add(Hint("No conditions — catch-all."));

        var addCond = new Button
        {
            Content = "+ condition",
            Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };
        addCond.Click += (_, _) =>
        {
            rule.All.Add(new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In });
            RebuildDetail();
            RebuildMaster();
        };
        DetailHost.Children.Add(addCond);

        var savePreset = new Button
        {
            Content = "Save as preset…",
            Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        savePreset.Click += (_, _) => OnSaveAsPreset(rule);
        DetailHost.Children.Add(savePreset);

        RefreshMatches();
    }

    private FrameworkElement BuildConditionRow(SortRule rule, RuleCondition cond)
    {
        // Line 1: [field ...............] [op] [✕]; line 2: [value full width].
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };

        var del = MoveButton("✕", "Remove condition", true, () => { rule.All.Remove(cond); RebuildDetail(); RebuildMaster(); });
        DockPanel.SetDock(del, Dock.Right);
        top.Children.Add(del);

        var ops = OpsFor[cond.Field];
        if (ops.Length == 1)
        {
            cond.Op = ops[0];
            var opWord = new TextBlock
            {
                Text = OpWord(cond.Op),
                Foreground = Palettes.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
            };
            DockPanel.SetDock(opWord, Dock.Right);
            top.Children.Add(opWord);
        }
        else
        {
            var op = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            FillOps(op, cond.Field, cond.Op);
            op.SelectionChanged += (_, _) => { if (op.SelectedItem is CompareOp o) { cond.Op = o; RebuildMaster(); RefreshMatches(); } };
            DockPanel.SetDock(op, Dock.Right);
            top.Children.Add(op);
        }

        var field = new ComboBox();
        foreach (ConditionField f in Enum.GetValues<ConditionField>()) field.Items.Add(f);
        field.SelectedItem = cond.Field;
        field.SelectionChanged += (_, _) =>
        {
            if (field.SelectedItem is not ConditionField nf || nf == cond.Field) return;
            cond.Field = nf;
            cond.Op = OpsFor[nf][0];
            RebuildDetail();
            RebuildMaster();
        };
        top.Children.Add(field); // no dock — fills the remaining width

        var value = new TextBox { Text = cond.Value };
        var hint = new TextBlock
        {
            Text = WatermarkFor(cond.Field),
            Foreground = Palettes.TextMuted,
            IsHitTestVisible = false,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = string.IsNullOrEmpty(cond.Value) ? Visibility.Visible : Visibility.Collapsed,
        };
        value.TextChanged += (_, _) =>
        {
            cond.Value = value.Text;
            hint.Visibility = string.IsNullOrEmpty(value.Text) ? Visibility.Visible : Visibility.Collapsed;
            MarkValue(value, cond);
            RebuildMaster();
            RefreshMatches();
        };
        MarkValue(value, cond);

        var cell = new Grid();
        cell.Children.Add(value);
        cell.Children.Add(hint);

        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        stack.Children.Add(top);
        stack.Children.Add(cell);
        return stack;
    }

    /// <summary>Shows, under the Destination box, which ${name} tokens the rule can fill from its
    /// Name regex groups — and flags any token in the path that has no matching group, since such a
    /// file would silently fall back to the target root.</summary>
    private void RebuildTokenHint(SortRule rule)
    {
        if (_tokenHint == null) return;
        var used = SortService.TokensIn(rule.Dest);
        var available = SortService.AvailableTokens(rule);
        if (used.Count == 0 && available.Count == 0)
        {
            _tokenHint.Visibility = Visibility.Collapsed;
            return;
        }
        _tokenHint.Visibility = Visibility.Visible;
        var missing = used.Where(t => !available.Contains(t)).ToArray();
        if (missing.Length > 0)
        {
            _tokenHint.Foreground = Palettes.Danger;
            _tokenHint.Text = "No such group: " + string.Join(", ", missing.Select(t => "${" + t + "}"))
                + " — add a (?<name>…) group in a Name regex condition, or the file goes to the root.";
        }
        else
        {
            _tokenHint.Foreground = Palettes.TextMuted;
            _tokenHint.Text = available.Count > 0
                ? "Tokens: " + string.Join(" ", available.OrderBy(t => t, StringComparer.Ordinal).Select(t => "${" + t + "}"))
                : "";
            _tokenHint.Visibility = _tokenHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── Preview (matches for the selected rule) ────────────────────────────

    private void OnPreviewChanged(object sender, TextChangedEventArgs e) => RefreshMatches();

    /// <summary>Dragging files onto the Test files box: show the copy cursor only for real files, so
    /// ordinary text dragging inside the box keeps working as before.</summary>
    private void OnPreviewFilesDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    /// <summary>Appends the dropped file paths to the box — one per line, after whatever is already
    /// there. The text change itself re-runs the preview.</summary>
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

    /// <summary>Runs the real SortService against the test paths and lists which of them the
    /// selected rule actually receives, so the preview can never diverge from real routing.</summary>
    private void RefreshMatches()
    {
        if (_matchesHost == null) return;
        _matchesHost.Children.Clear();
        // The primary separator is a newline (the box is labeled "one path per line"), but we also
        // tolerate commas and semicolons when a list is pasted. A space is not a separator: spaces
        // are legal in paths and file names.
        var files = (PreviewInput.Text ?? "")
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (files.Length == 0)
        {
            _matchesHost.Children.Add(Hint("Paste file names into Test files to preview routing."));
            return;
        }
        if (_selected < 0 || _selected >= _rules.Count) return;
        // A rule with no conditions (catch-all) matches every file, so anything after the first such
        // rule is unreachable. Show an explicit warning — otherwise "Routes here: 0 of N" on a lower
        // rule looks inexplicable.
        int firstCatchAll = -1;
        for (int i = 0; i < _rules.Count; i++)
            if (_rules[i].All.Count == 0) { firstCatchAll = i; break; }
        if (firstCatchAll >= 0 && _selected > firstCatchAll)
            _matchesHost.Children.Add(Hint(
                $"Unreachable: rule {firstCatchAll + 1} ({FriendlyDest(_rules[firstCatchAll].Dest)}) " +
                "is a catch-all and takes every file first. Move it down or add conditions."));
        // Count by rule index, not by destination folder: two rules with the same Dest no longer
        // attribute each other's files.
        var here = files.Where(f => SortService.MatchedRuleIndex(_rules, f) == _selected).ToArray();
        _matchesHost.Children.Add(new TextBlock
        {
            Text = $"Routes here: {here.Length} of {files.Length}",
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            Margin = new Thickness(0, 0, 0, 2),
        });
        bool showDest = _rules[_selected].Dest.Contains("${", StringComparison.Ordinal);
        foreach (var f in here)
            _matchesHost.Children.Add(new TextBlock
            {
                Text = showDest
                    ? $"{Path.GetFileName(f)}  →  {ResolvedDestLabel(_rules[_selected], Path.GetFileName(f))}"
                    : Path.GetFileName(f),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        // Size/age conditions are treated as zero in the preview: the test input is names without
        // real files on disk. Warn if any rule has such a condition — a file might have been silently
        // caught by a higher-priority rule rather than the selected one.
        if (_rules.Any(r => r.All.Any(c => c.Field is ConditionField.SizeMb or ConditionField.AgeDays)))
            _matchesHost.Children.Add(Hint(
                "Size/age here are treated as 0 — test names are not real files, so these preview only by extension/name."));
    }

    /// <summary>The subfolder a matched file resolves to once its tokens are expanded, for the
    /// preview. Unfilled tokens mean the file lands at the target root.</summary>
    private static string ResolvedDestLabel(SortRule rule, string fileName)
    {
        var expanded = SortService.ExpandTemplate(rule, fileName, out bool ok);
        return ok ? expanded : "(root — unfilled tokens)";
    }

    // ── Presets ────────────────────────────────────────────────────────────

    /// <summary>Opens a menu of file-type presets from config. Each item appends a ready rule;
    /// "Add all categories" seeds one rule per preset.</summary>
    private void OnPresetsClick(object sender, RoutedEventArgs e)
    {
        var presets = TargetStore.Config.Presets ?? PresetService.Defaults();
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        foreach (var p in presets)
        {
            var item = new MenuItem { Header = $"{p.Name}  ({p.Extensions})" };
            var captured = p;
            item.Click += (_, _) => AddPresetRule(captured);
            menu.Items.Add(item);
        }
        if (presets.Count > 0)
        {
            menu.Items.Add(new Separator());
            var all = new MenuItem { Header = "Add all categories" };
            all.Click += (_, _) =>
            {
                foreach (var p in presets) _rules.Add(RuleFromPreset(p));
                _selected = _rules.Count - 1;
                RebuildMaster();
                RebuildDetail();
            };
            menu.Items.Add(all);
        }
        menu.IsOpen = true;
    }

    private void AddPresetRule(FilePreset p)
    {
        _rules.Add(RuleFromPreset(p));
        _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
    }

    private static SortRule RuleFromPreset(FilePreset p) => new()
    {
        Dest = p.Dest,
        All = { new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In, Value = p.Extensions } },
    };

    /// <summary>Saves the current rule as a named user preset in config. Presets are
    /// extension-based, so the rule needs an Extension condition. Same name overwrites.</summary>
    private void OnSaveAsPreset(SortRule rule)
    {
        var ext = rule.All.FirstOrDefault(c => c.Field == ConditionField.Extension)?.Value;
        if (string.IsNullOrWhiteSpace(ext))
        {
            MessageBox.Show(this, "Presets are extension-based — add an Extension condition first.",
                "Save as preset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var prompt = new PromptWindow("Save as preset", "Preset name:") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var name = prompt.Value.Trim();
        if (name.Length == 0) return;
        var presets = TargetStore.Config.Presets ??= PresetService.Defaults();
        presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        presets.Add(PresetService.FromRule(name, rule)!);
        TargetStore.Save();
    }

    // ── Shared helpers ─────────────────────────────────────────────────────

    private void MoveSelected(int delta)
    {
        int to = _selected + delta;
        if (to < 0 || to >= _rules.Count) return;
        (_rules[_selected], _rules[to]) = (_rules[to], _rules[_selected]);
        _selected = to;
        RebuildMaster();
        RebuildDetail();
    }

    private void DeleteSelected()
    {
        if (_selected < 0 || _selected >= _rules.Count) return;
        _rules.RemoveAt(_selected);
        if (_selected >= _rules.Count) _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
    }

    private void BrowseDest(SortRule rule)
    {
        using var dlg = new WF.FolderBrowserDialog();
        if (dlg.ShowDialog() != WF.DialogResult.OK) return;
        rule.Dest = dlg.SelectedPath;
        RebuildDetail();
        RebuildMaster();
    }

    private static void FillOps(ComboBox op, ConditionField field, CompareOp current)
    {
        op.Items.Clear();
        foreach (var o in OpsFor[field]) op.Items.Add(o);
        op.SelectedItem = OpsFor[field].Contains(current) ? current : OpsFor[field][0];
    }

    private Button MoveButton(string glyph, string name, bool enabled, Action onClick)
    {
        var b = new Button
        {
            Content = glyph,
            Width = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0),
            IsEnabled = enabled,
            ToolTip = name, // the glyph alone tells a screen reader nothing; name the action
        };
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Flags an invalid regex value with a red border; otherwise shows the syntax hint.</summary>
    private static void MarkValue(TextBox box, RuleCondition cond)
    {
        bool bad = cond.Field == ConditionField.NameRegex && !IsValidRegex(cond.Value);
        box.BorderBrush = bad ? Palettes.Danger : Palettes.Border;
        box.ToolTip = bad ? "Invalid regular expression" : HintFor(cond.Field);
    }

    private static bool IsValidRegex(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        try { _ = new Regex(pattern); return true; }
        catch (RegexParseException) { return false; }
    }

    /// <summary>Blocks Save when a condition value is empty, an unparsable number, or a bad regex.</summary>
    private bool TryValidateRules(out string error)
    {
        for (int i = 0; i < _rules.Count; i++)
        {
            foreach (var c in _rules[i].All)
            {
                if (string.IsNullOrWhiteSpace(c.Value))
                { error = $"Rule {i + 1}: a condition has an empty value."; return false; }
                if (c.Field == ConditionField.NameRegex && !IsValidRegex(c.Value))
                { error = $"Rule {i + 1}: invalid regular expression."; return false; }
                if ((c.Field == ConditionField.SizeMb || c.Field == ConditionField.AgeDays)
                    && !double.TryParse(c.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                { error = $"Rule {i + 1}: '{c.Value}' is not a number."; return false; }
            }
            var available = SortService.AvailableTokens(_rules[i]);
            foreach (var tok in SortService.TokensIn(_rules[i].Dest))
                if (!available.Contains(tok))
                { error = $"Rule {i + 1}: destination uses ${{{tok}}} but no Name regex has a (?<{tok}>…) group."; return false; }
        }
        error = "";
        return true;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = Palettes.TextMuted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static string FieldWord(ConditionField f) => f switch
    {
        ConditionField.Extension => "ext",
        ConditionField.NameContains => "name",
        ConditionField.NameRegex => "regex",
        ConditionField.SizeMb => "size",
        ConditionField.AgeDays => "age",
        _ => "",
    };

    private static string OpWord(CompareOp op) => op switch
    {
        CompareOp.In => "is",
        CompareOp.Contains => "contains",
        CompareOp.Matches => "matches",
        _ => op.ToString().ToLowerInvariant(),
    };

    /// <summary>Short in-field placeholder shown while the value is empty.</summary>
    private static string WatermarkFor(ConditionField f) => f switch
    {
        ConditionField.Extension => "png jpg webp",
        ConditionField.NameContains => "part of the name",
        ConditionField.NameRegex => "^IMG_\\d+",
        ConditionField.SizeMb => "10",
        ConditionField.AgeDays => "30",
        _ => "",
    };

    /// <summary>Full syntax description shown as the value box tooltip.</summary>
    private static string HintFor(ConditionField f) => f switch
    {
        ConditionField.Extension => "Extensions separated by space or comma; leading dots optional (png, .jpg, webp)",
        ConditionField.NameContains => "A substring of the file name (case-insensitive)",
        ConditionField.NameRegex => "A .NET regular expression matched against the file name, e.g. ^IMG_\\d+",
        ConditionField.SizeMb => "A size threshold in megabytes, e.g. 10",
        ConditionField.AgeDays => "An age threshold in days since last change, e.g. 30",
        _ => "",
    };
}
