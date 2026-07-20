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
        EnableResizableLayout();
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

    /// <summary>Switches the editor from the compact single-column form (a simple target) to the
    /// resizable multi-pane sorter layout: a resizable window with star-sized columns, the two column
    /// splitters and the preview splitter, and a growable preview row. The simple-target view keeps the
    /// window sized to its content, so the extra panes never leave empty gaps there.</summary>
    private void EnableResizableLayout()
    {
        double prevW = ActualWidth, prevH = ActualHeight;
        bool wasShown = IsLoaded;

        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        MinWidth = 900;
        MinHeight = 600;
        if (double.IsNaN(Width) || Width < 1040) Width = 1040;
        if (double.IsNaN(Height) || Height < 720) Height = 720;

        FieldsScroll.ClearValue(WidthProperty); // let the star column drive the field panel width
        FieldsCol.Width = new GridLength(2, GridUnitType.Star);
        FieldsCol.MinWidth = 260;
        RulesCol.Width = new GridLength(1.4, GridUnitType.Star);
        RulesCol.MinWidth = 180;
        DetailCol.Width = new GridLength(3, GridUnitType.Star);
        DetailCol.MinWidth = 360;
        Splitter1.Visibility = Visibility.Visible;
        Splitter2.Visibility = Visibility.Visible;

        MainRow.Height = new GridLength(1, GridUnitType.Star);
        MainRow.MinHeight = 220;
        PreviewSplitter.Visibility = Visibility.Visible;
        PreviewRow.Height = new GridLength(170);
        PreviewRow.MinHeight = 80;

        // When Convert grows an already-shown window, WindowStartupLocation no longer fires, so the window
        // would extend down-right from its compact top-left and can run off-screen. Re-center it on its
        // former middle and clamp it back onto the work area.
        if (wasShown) KeepOnScreenAfterGrow(prevW, prevH);
    }

    /// <summary>Keeps a just-enlarged window centered on its previous middle and fully inside the work
    /// area — used when the editor grows from the compact form to the resizable sorter layout after it is
    /// already shown (the Convert button), where WindowStartupLocation does not re-run.</summary>
    private void KeepOnScreenAfterGrow(double prevW, double prevH)
    {
        Left -= (Width - prevW) / 2;
        Top -= (Height - prevH) / 2;
        var wa = SystemParameters.WorkArea;
        Left = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
        Top = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
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
        FrameworkElement? selectedRow = null;
        for (int i = 0; i < _rules.Count; i++)
        {
            var row = BuildMasterRow(_rules[i], i);
            MasterHost.Children.Add(row);
            if (i == _selected) selectedRow = row;
        }
        // After a programmatic select (add, duplicate or move) the chosen row can land outside the
        // scrolled list; bring it into view once layout settles so BringIntoView sees final positions.
        if (selectedRow != null)
            Dispatcher.BeginInvoke(new Action(() => selectedRow.BringIntoView()),
                System.Windows.Threading.DispatcherPriority.Loaded);
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
        var browse = new Button { Content = "…", Width = 28, Margin = new Thickness(6, 0, 0, 0) };
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
            _tokenHint.Text = "Bad token format: " + string.Join(", ", badFormats)
                + " — dates take a .NET format (dd-MM-yy), ${stem:N} a number, ${size:…} a bucket spec.";
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
        _tokenChips.Children.Add(HelpChip());
    }

    /// <summary>A trailing chip that opens the read-only token reference, so every token — including the
    /// f-/c- date twins kept off the chip row — and its format and limits are one click away.</summary>
    private Border HelpChip()
    {
        var chip = new Border
        {
            BorderBrush = Palettes.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(6, 1, 6, 1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock { Text = "? tokens", FontSize = 11, Foreground = Palettes.Accent },
            ToolTip = "Open the destination token reference",
        };
        chip.MouseLeftButtonUp += (_, _) => new TokenHelpWindow { Owner = this }.ShowDialog();
        return chip;
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
