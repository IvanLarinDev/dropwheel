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
            Text = MasterSummary(rule), FontSize = 11, Foreground = Palettes.TextMuted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var body = new StackPanel { Margin = new Thickness(8, 4, 6, 4) };
        body.Children.Add(name);
        body.Children.Add(summary);

        var border = new Border
        {
            Child = body, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 3),
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
        _matchesHost = null;
        if (_selected < 0 || _selected >= _rules.Count)
        {
            DetailHost.Children.Add(Hint("Select or add a rule."));
            return;
        }
        var rule = _rules[_selected];

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(tools, Dock.Right);
        tools.Children.Add(MoveButton("▲", _selected > 0, () => MoveSelected(-1)));
        tools.Children.Add(MoveButton("▼", _selected < _rules.Count - 1, () => MoveSelected(+1)));
        tools.Children.Add(MoveButton("✕", true, DeleteSelected));
        header.Children.Add(tools);
        header.Children.Add(new TextBlock { Text = $"Rule {_selected + 1}", FontWeight = FontWeights.SemiBold });
        DetailHost.Children.Add(header);

        DetailHost.Children.Add(new TextBlock { Text = "Destination (subfolder or absolute)", FontSize = 11 });
        var destRow = new DockPanel { Margin = new Thickness(0, 2, 0, 10) };
        var browse = new Button { Content = "…", Width = 26, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) => BrowseDest(rule);
        var destBox = new TextBox { Text = rule.Dest };
        destBox.TextChanged += (_, _) => { rule.Dest = destBox.Text; RebuildMaster(); RefreshMatches(); };
        destRow.Children.Add(browse);
        destRow.Children.Add(destBox);
        DetailHost.Children.Add(destRow);

        DetailHost.Children.Add(new TextBlock { Text = "Conditions (all must match)", FontSize = 11 });
        foreach (var c in rule.All)
            DetailHost.Children.Add(BuildConditionRow(rule, c));
        if (rule.All.Count == 0)
            DetailHost.Children.Add(Hint("No conditions — catch-all."));

        var addCond = new Button
        {
            Content = "+ condition", Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
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
            Content = "Save as preset…", Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0),
        };
        savePreset.Click += (_, _) => OnSaveAsPreset(rule);
        DetailHost.Children.Add(savePreset);

        DetailHost.Children.Add(new Border
        {
            BorderBrush = Palettes.Border, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 6),
        });
        _matchesHost = new StackPanel();
        DetailHost.Children.Add(_matchesHost);
        RefreshMatches();
    }

    private FrameworkElement BuildConditionRow(SortRule rule, RuleCondition cond)
    {
        // Line 1: [field ...............] [op] [✕]; line 2: [value full width].
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };

        var del = MoveButton("✕", true, () => { rule.All.Remove(cond); RebuildDetail(); RebuildMaster(); });
        DockPanel.SetDock(del, Dock.Right);
        top.Children.Add(del);

        var ops = OpsFor[cond.Field];
        if (ops.Length == 1)
        {
            cond.Op = ops[0];
            var opWord = new TextBlock
            {
                Text = OpWord(cond.Op), Foreground = Palettes.TextMuted, VerticalAlignment = VerticalAlignment.Center,
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
            Text = WatermarkFor(cond.Field), Foreground = Palettes.TextMuted, IsHitTestVisible = false,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
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

    // ── Preview (matches for the selected rule) ────────────────────────────

    private void OnPreviewChanged(object sender, TextChangedEventArgs e) => RefreshMatches();

    /// <summary>Runs the real SortService against the test paths and lists which of them the
    /// selected rule actually receives, so the preview can never diverge from real routing.</summary>
    private void RefreshMatches()
    {
        if (_matchesHost == null) return;
        _matchesHost.Children.Clear();
        // Основной разделитель — перевод строки (поле подписано «one path per line»), но
        // терпим и запятую с точкой-с-запятой при вставке списка. Пробел не разделитель:
        // в путях и именах файлов пробелы законны.
        var files = (PreviewInput.Text ?? "")
            .Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (files.Length == 0)
        {
            _matchesHost.Children.Add(Hint("Paste file names into Test files to preview routing."));
            return;
        }
        if (_selected < 0 || _selected >= _rules.Count) return;
        // Считаем по номеру правила, а не по папке назначения: два правила с одинаковым
        // Dest больше не приписывают друг другу файлы.
        var here = files.Where(f => SortService.MatchedRuleIndex(_rules, f) == _selected).ToArray();
        _matchesHost.Children.Add(new TextBlock
        {
            Text = $"Routes here: {here.Length} of {files.Length}", FontSize = 11, Foreground = Palettes.TextMuted,
            Margin = new Thickness(0, 0, 0, 2),
        });
        foreach (var f in here)
            _matchesHost.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(f), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            });
        // Условия по размеру/возрасту в предпросмотре считаются нулём: тестовый ввод — это
        // имена без реальных файлов на диске. Предупреждаем, если такое условие есть в ЛЮБОМ
        // правиле — файл мог молча перехватить другое правило выше по приоритету, а не выбранное.
        if (_rules.Any(r => r.All.Any(c => c.Field is ConditionField.SizeMb or ConditionField.AgeDays)))
            _matchesHost.Children.Add(Hint(
                "Size/age here are treated as 0 — test names are not real files, so these preview only by extension/name."));
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

    private Button MoveButton(string glyph, bool enabled, Action onClick)
    {
        var b = new Button
        {
            Content = glyph, Width = 24, Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(0), IsEnabled = enabled,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Flags an invalid regex value with a red border; otherwise shows the syntax hint.</summary>
    private static void MarkValue(TextBox box, RuleCondition cond)
    {
        bool bad = cond.Field == ConditionField.NameRegex && !IsValidRegex(cond.Value);
        box.BorderBrush = bad ? Brushes.Firebrick : Palettes.Border;
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
        }
        error = "";
        return true;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text, FontSize = 11, Foreground = Palettes.TextMuted, TextWrapping = TextWrapping.Wrap,
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
