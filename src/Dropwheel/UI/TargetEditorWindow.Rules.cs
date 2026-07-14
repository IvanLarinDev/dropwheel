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
    private TextBox? _destBox;
    private WrapPanel? _tokenChips;

    /// <summary>Built-in tokens offered as clickable chips. A curated, common subset — the f-/c-prefixed
    /// date twins stay in the destination box tooltip to keep the chip row short.</summary>
    private static readonly string[] BuiltinChipTokens =
        { "date", "year", "month", "day", "time", "week", "quarter", "ext", "stem", "initial", "size", "slug" };

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
        [ConditionField.CreatedDaysAgo] = new[] { CompareOp.Gt, CompareOp.Lt, CompareOp.Gte, CompareOp.Lte },
        [ConditionField.MediaKind] = new[] { CompareOp.In },
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
            Opacity = rule.Enabled ? 1.0 : 0.45,
        };
        border.MouseLeftButtonUp += (_, _) => { _selected = index; RebuildMaster(); RebuildDetail(); };
        return border;
    }

    private static string MasterSummary(SortRule rule)
    {
        var body = rule.All.Count == 0
            ? "catch-all"
            : string.Join(", ", rule.All.Select(c => $"{(c.Negate ? "not " : "")}{FieldWord(c.Field)} {ShortValue(c)}"));
        var scoped = rule.Scope == RuleScope.Files ? body : $"[{ScopeWord(rule.Scope).ToLowerInvariant()}] {body}";
        return rule.Enabled ? scoped : "(off) " + scoped;
    }

    private static string ScopeWord(RuleScope scope) => scope switch
    {
        RuleScope.Files => "Files",
        RuleScope.Folders => "Folders",
        RuleScope.Both => "Files and folders",
        _ => "Files",
    };

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
        tools.Children.Add(MoveButton("❐", "Duplicate rule", true, DuplicateSelected));
        tools.Children.Add(MoveButton("✕", "Delete rule", true, DeleteSelected));
        header.Children.Add(tools);
        header.Children.Add(new TextBlock { Text = $"Rule {_selected + 1}", FontWeight = FontWeights.SemiBold });
        DetailHost.Children.Add(header);

        var enabled = new CheckBox
        {
            Content = "Enabled",
            IsChecked = rule.Enabled,
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Turn the rule off without deleting it — a disabled rule is skipped during sorting.",
        };
        enabled.Checked += (_, _) => { rule.Enabled = true; RebuildMaster(); RefreshMatches(); };
        enabled.Unchecked += (_, _) => { rule.Enabled = false; RebuildMaster(); RefreshMatches(); };
        DetailHost.Children.Add(enabled);

        DetailHost.Children.Add(new TextBlock { Text = "Applies to", FontSize = 11 });
        var scopeBox = new ComboBox
        {
            Margin = new Thickness(0, 2, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 150,
            ToolTip = "Which dropped items this rule may catch. Extension conditions never match a "
                    + "folder, and size is 0 for folders.",
        };
        var scopes = new[] { RuleScope.Files, RuleScope.Folders, RuleScope.Both };
        foreach (var s in scopes) scopeBox.Items.Add(new ComboBoxItem { Content = ScopeWord(s), Tag = s });
        scopeBox.SelectedIndex = Array.IndexOf(scopes, rule.Scope);
        scopeBox.SelectionChanged += (_, _) =>
        {
            if (scopeBox.SelectedItem is ComboBoxItem { Tag: RuleScope s })
            { rule.Scope = s; RebuildMaster(); RefreshMatches(); }
        };
        DetailHost.Children.Add(scopeBox);

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
                    + "e.g. episodes\\${ep}\\${sq}\\${sh}. Built-in tokens — drop time: ${date} ${year} "
                    + "${month} ${day} ${time} ${week} ${quarter}; file's modified date: an f- prefix "
                    + "(${fyear} ${fmonth}…); file's created date: a c- prefix (${cyear} ${cmonth}…); "
                    + "file name: ${ext} ${stem} ${initial}. Add a .NET format after a colon on a date "
                    + "token, e.g. ${date:dd-MM-yy} or ${month:MMMM}.",
        };
        destBox.TextChanged += (_, _) => { rule.Dest = destBox.Text; RebuildMaster(); RebuildTokenHint(rule); RefreshMatches(); };
        _destBox = destBox;
        destRow.Children.Add(browse);
        destRow.Children.Add(destBox);
        DetailHost.Children.Add(destRow);

        _tokenChips = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        DetailHost.Children.Add(_tokenChips);

        _tokenHint = new TextBlock
        {
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DetailHost.Children.Add(_tokenHint);
        RebuildTokenHint(rule);

        DetailHost.Children.Add(new TextBlock
        {
            Text = "Built-in: ${date} ${year} ${month} ${week} ${quarter} · file modified ${fdate}… · "
                 + "file created ${cdate}… · ${ext} ${stem} ${initial}. Format after a colon: ${date:dd-MM-yy}.",
            FontSize = 11,
            Foreground = Palettes.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

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
            op.SelectionChanged += (_, _) => { if (op.SelectedItem is ComboBoxItem { Tag: CompareOp o }) { cond.Op = o; RebuildMaster(); RefreshMatches(); } };
            DockPanel.SetDock(op, Dock.Right);
            top.Children.Add(op);
        }

        var not = new CheckBox
        {
            Content = "not",
            IsChecked = cond.Negate,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Invert this condition — the rule counts it as met when it does NOT hold.",
        };
        not.Checked += (_, _) => { cond.Negate = true; RebuildMaster(); RefreshMatches(); };
        not.Unchecked += (_, _) => { cond.Negate = false; RebuildMaster(); RefreshMatches(); };
        DockPanel.SetDock(not, Dock.Left);
        top.Children.Add(not);

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

        FrameworkElement cell;
        if (cond.Field == ConditionField.MediaKind)
        {
            cell = BuildMediaKindPicker(cond);
        }
        else
        {
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

            var grid = new Grid();
            grid.Children.Add(value);
            grid.Children.Add(hint);
            cell = grid;
        }

        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        stack.Children.Add(top);
        stack.Children.Add(cell);
        return stack;
    }

    /// <summary>The value control for a media-kind condition: a dropdown of the known kinds instead of a
    /// free-text box. Seeds the first kind when the condition has no valid value yet — e.g. right after
    /// the field was switched over from extension or name.</summary>
    private FrameworkElement BuildMediaKindPicker(RuleCondition cond)
    {
        var kinds = SortService.MediaKinds;
        int idx = 0;
        for (int i = 0; i < kinds.Count; i++)
            if (string.Equals(kinds[i], cond.Value, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        cond.Value = kinds[idx];

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 150 };
        foreach (var k in kinds) combo.Items.Add(new ComboBoxItem { Content = char.ToUpperInvariant(k[0]) + k[1..], Tag = k });
        combo.SelectedIndex = idx;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string k }) { cond.Value = k; RebuildMaster(); RefreshMatches(); }
        };
        return combo;
    }

    /// <summary>Shows, under the Destination box, which ${name} tokens the rule can fill from its
    /// Name regex groups — and flags any token in the path that has no matching group, since such a
    /// file would silently fall back to the target root.</summary>
    private void RebuildTokenHint(SortRule rule)
    {
        if (_tokenHint == null) return;
        RebuildTokenChips(rule);
        var used = SortService.TokensIn(rule.Dest);
        var available = SortService.AvailableTokens(rule);
        var missing = used.Where(t => !available.Contains(t) && !SortService.BuiltinTokens.Contains(t)).ToArray();
        var badFormats = SortService.ParseTokens(rule.Dest)
            .Where(p => SortService.TokenTakesFormat(p.Name) && !SortService.IsValidTokenFormat(p.Name, p.Format))
            .Select(p => "${" + p.Name + ":" + p.Format + "}")
            .Distinct()
            .ToArray();
        if (missing.Length > 0)
        {
            _tokenHint.Foreground = Palettes.Danger;
            _tokenHint.Text = "No such group: " + string.Join(", ", missing.Select(t => "${" + t + "}"))
                + " — add a (?<name>…) group in a Name regex condition, or the file goes to the root.";
            _tokenHint.Visibility = Visibility.Visible;
        }
        else if (badFormats.Length > 0)
        {
            _tokenHint.Foreground = Palettes.Danger;
            _tokenHint.Text = "Bad date format: " + string.Join(", ", badFormats)
                + " — use a .NET format like dd-MM-yy or yyyy-MM.";
            _tokenHint.Visibility = Visibility.Visible;
        }
        else
        {
            _tokenHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Fills the chip row under the destination box with clickable tokens: first this rule's own
    /// Name-regex groups (accent-bordered), then the common built-in tokens. Clicking a chip inserts it
    /// into the destination at the caret, so the tokens are discoverable and typed in one click.</summary>
    private void RebuildTokenChips(SortRule rule)
    {
        if (_tokenChips == null) return;
        _tokenChips.Children.Clear();
        foreach (var g in SortService.AvailableTokens(rule).OrderBy(t => t, StringComparer.Ordinal))
            _tokenChips.Children.Add(TokenChip(g, isGroup: true));
        foreach (var b in BuiltinChipTokens)
            _tokenChips.Children.Add(TokenChip(b, isGroup: false));
    }

    private Border TokenChip(string name, bool isGroup)
    {
        var chip = new Border
        {
            Background = Palettes.Selection,
            BorderBrush = isGroup ? Palettes.Accent : Palettes.TextMuted,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(6, 1, 6, 1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock { Text = "${" + name + "}", FontSize = 11, Foreground = Palettes.TextMuted },
            ToolTip = isGroup
                ? $"Insert ${{{name}}} — a group from this rule's Name regex"
                : $"Insert ${{{name}}} — a built-in token",
        };
        chip.MouseLeftButtonUp += (_, _) => InsertToken("${" + name + "}");
        return chip;
    }

    /// <summary>Inserts a token into the destination box at the caret and keeps focus, letting its
    /// TextChanged refresh the preview and hint.</summary>
    private void InsertToken(string token)
    {
        if (_destBox == null) return;
        int at = _destBox.SelectionStart;
        _destBox.Text = _destBox.Text.Insert(at, token);
        _destBox.SelectionStart = at + token.Length;
        _destBox.Focus();
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
        // A file-scope rule with no conditions (catch-all) matches every file, so anything after the
        // first such rule is unreachable. A folders-only catch-all does not take the test files, so it
        // does not count here. Show an explicit warning — otherwise "Routes here: 0 of N" on a lower
        // rule looks inexplicable.
        int firstCatchAll = -1;
        for (int i = 0; i < _rules.Count; i++)
            if (_rules[i].All.Count == 0 && SortService.ScopeIncludes(_rules[i].Scope, isDirectory: false))
            { firstCatchAll = i; break; }
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
                    ? $"{Path.GetFileName(f)}  →  {ResolvedDestLabel(_rules[_selected], f)}"
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
        // File-date tokens need the real file on disk; a pasted test name that is not an existing path
        // has no modified/created time, so it previews as a fall-back to the root rather than a dated folder.
        if (SortService.ParseTokens(_rules[_selected].Dest)
                .Any(p => (p.Name.StartsWith('f') || p.Name.StartsWith('c')) && SortService.BuiltinTokens.Contains(p.Name)))
            _matchesHost.Children.Add(Hint(
                "File-date tokens (${fdate}… / ${cdate}…) resolve from the real file at drop time; a test name with no real file routes to the root here."));
    }

    /// <summary>The subfolder a matched file resolves to once its tokens are expanded, for the
    /// preview. Unfilled tokens mean the file lands at the target root.</summary>
    private static string ResolvedDestLabel(SortRule rule, string filePath)
    {
        var expanded = SortService.ExpandTemplate(rule, filePath, out bool ok);
        return ok ? expanded : "(root — unfilled tokens)";
    }

    // ── Presets ────────────────────────────────────────────────────────────

    /// <summary>Ready catch-all rules that route every file into a dated folder. These live in code,
    /// not config, so they are always offered regardless of the user's saved presets. Each is just a
    /// destination built from built-in date tokens; the user can edit or add conditions afterwards.</summary>
    private static readonly (string Label, string Dest)[] DatedPresets =
    {
        ("Today's date  —  ${date}", "${date}"),
        ("Year \\ month  —  ${year}\\${month}", "${year}\\${month}"),
        ("Year \\ week  —  ${year}\\W${week}", "${year}\\W${week}"),
        ("Year \\ quarter  —  ${year}\\${quarter}", "${year}\\${quarter}"),
        ("By file's created month  —  ${cyear}\\${cmonth}", "${cyear}\\${cmonth}"),
        ("By file's modified month  —  ${fyear}\\${fmonth}", "${fyear}\\${fmonth}"),
    };

    /// <summary>Ready catch-all rules that route a file into a size-bucket folder. A bare ${size} uses the
    /// built-in buckets; the custom entry seeds the editable spec syntax "name limit, …, name" so the
    /// user can rename buckets and move the megabyte boundaries.</summary>
    private static readonly (string Label, string Dest)[] SizePresets =
    {
        ("Default buckets  —  ${size}", "by-size\\${size}"),
        ("Custom buckets  —  ${size: tiny 0.5, …}", "by-size\\${size: tiny 0.5, small 10, medium 100, large 1000, huge}"),
    };

    /// <summary>Opens the presets menu with two groups: "By extension" holds the file-type categories
    /// from config (plus "Add all categories"), and "Dated folders" offers destinations built from the
    /// built-in date tokens.</summary>
    private void OnPresetsClick(object sender, RoutedEventArgs e)
    {
        var presets = TargetStore.Config.Presets ?? PresetService.Defaults();
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };

        var byExt = new MenuItem { Header = "By extension" };
        foreach (var p in presets)
        {
            var item = new MenuItem { Header = $"{p.Name}  ({p.Extensions})" };
            var captured = p;
            item.Click += (_, _) => AddPresetRule(captured);
            byExt.Items.Add(item);
        }
        if (presets.Count > 0)
        {
            byExt.Items.Add(new Separator());
            var all = new MenuItem { Header = "Add all categories" };
            all.Click += (_, _) =>
            {
                foreach (var p in presets) _rules.Add(RuleFromPreset(p));
                _selected = _rules.Count - 1;
                RebuildMaster();
                RebuildDetail();
            };
            byExt.Items.Add(all);
        }
        menu.Items.Add(byExt);

        var dated = new MenuItem { Header = "Dated folders" };
        foreach (var (label, dest) in DatedPresets)
        {
            var item = new MenuItem { Header = label };
            var captured = dest;
            item.Click += (_, _) => AddDatedRule(captured);
            dated.Items.Add(item);
        }
        menu.Items.Add(dated);

        var bySize = new MenuItem { Header = "By size" };
        foreach (var (label, dest) in SizePresets)
        {
            var item = new MenuItem { Header = label };
            var captured = dest;
            item.Click += (_, _) => AddSizeRule(captured);
            bySize.Items.Add(item);
        }
        menu.Items.Add(bySize);
        menu.IsOpen = true;
    }

    /// <summary>Appends a catch-all rule that routes every file and folder into a dated destination.
    /// Scope is Both so the dated presets sort folders too — the point of dating.</summary>
    private void AddDatedRule(string dest)
    {
        _rules.Add(new SortRule { Dest = dest, Scope = RuleScope.Both });
        _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
    }

    /// <summary>Appends a catch-all rule that routes a file into a size-bucket folder. Scope stays Files:
    /// a folder has no meaningful size, so the size token would send it to the root anyway.</summary>
    private void AddSizeRule(string dest)
    {
        _rules.Add(new SortRule { Dest = dest, Scope = RuleScope.Files });
        _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
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
            DwMessageBox.Show(this, "Save as preset",
                "Presets are extension-based — add an Extension condition first.");
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

    /// <summary>Inserts an independent copy of the selected rule right after it and selects the copy, so
    /// a similar rule takes one click instead of re-entering every condition.</summary>
    private void DuplicateSelected()
    {
        if (_selected < 0 || _selected >= _rules.Count) return;
        _rules.Insert(_selected + 1, _rules[_selected].Clone());
        _selected++;
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
        var ops = OpsFor[field];
        foreach (var o in ops) op.Items.Add(new ComboBoxItem { Content = OpWord(o), Tag = o });
        op.SelectedIndex = Math.Max(0, Array.IndexOf(ops, current));
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
                if ((c.Field == ConditionField.SizeMb || c.Field == ConditionField.AgeDays
                        || c.Field == ConditionField.CreatedDaysAgo)
                    && !double.TryParse(c.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                { error = $"Rule {i + 1}: '{c.Value}' is not a number."; return false; }
            }
            var available = SortService.AvailableTokens(_rules[i]);
            foreach (var (name, format) in SortService.ParseTokens(_rules[i].Dest))
            {
                if (!available.Contains(name) && !SortService.BuiltinTokens.Contains(name))
                { error = $"Rule {i + 1}: destination uses ${{{name}}} but no Name regex has a (?<{name}>…) group."; return false; }
                if (SortService.TokenTakesFormat(name) && !SortService.IsValidTokenFormat(name, format))
                {
                    error = name == "size"
                        ? $"Rule {i + 1}: '{format}' is not a valid size spec — use \"name limit, …, name\" with rising limits, e.g. \"tiny 0.5, small 10, huge\"."
                        : $"Rule {i + 1}: '{format}' is not a valid date format for ${{{name}}}.";
                    return false;
                }
            }
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
        ConditionField.CreatedDaysAgo => "created",
        ConditionField.MediaKind => "type",
        _ => "",
    };

    private static string OpWord(CompareOp op) => op switch
    {
        CompareOp.In => "is",
        CompareOp.Contains => "contains",
        CompareOp.Matches => "matches",
        CompareOp.Gt => ">",
        CompareOp.Lt => "<",
        CompareOp.Gte => "≥",
        CompareOp.Lte => "≤",
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
        ConditionField.CreatedDaysAgo => "30",
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
        ConditionField.CreatedDaysAgo => "An age threshold in days since the file was created, e.g. 30",
        _ => "",
    };
}
