using System.Windows;
using System.Windows.Controls;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class TargetEditorWindow
{
    private void OnPresetsClick(object sender, RoutedEventArgs e)
    {
        var presets = TargetStore.Config.Presets ?? PresetService.Defaults();
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };

        var byExtension = new MenuItem { Header = "By extension" };
        foreach (var preset in presets)
        {
            var item = new MenuItem { Header = $"{preset.Name}  ({preset.Extensions})" };
            var captured = preset;
            item.Click += (_, _) => AddPresetRule(captured);
            byExtension.Items.Add(item);
        }
        if (presets.Count > 0)
        {
            byExtension.Items.Add(new Separator());
            var addAll = new MenuItem { Header = "Add all categories" };
            addAll.Click += (_, _) =>
            {
                foreach (var preset in presets) _rules.Add(RuleFromPreset(preset));
                SelectLastRule();
            };
            byExtension.Items.Add(addAll);
        }
        menu.Items.Add(byExtension);

        menu.Items.Add(PresetGroup(
            "Dated folders",
            BuiltInRulePresets.Dated,
            destination => AddCatchAllRule(destination, RuleScope.Both)));
        menu.Items.Add(PresetGroup(
            "By size",
            BuiltInRulePresets.BySize,
            destination => AddCatchAllRule(destination, RuleScope.Files)));
        menu.IsOpen = true;
    }

    private static MenuItem PresetGroup(
        string header,
        IEnumerable<(string Label, string Destination)> presets,
        Action<string> add)
    {
        var group = new MenuItem { Header = header };
        foreach (var (label, destination) in presets)
        {
            var item = new MenuItem { Header = label };
            var captured = destination;
            item.Click += (_, _) => add(captured);
            group.Items.Add(item);
        }
        return group;
    }

    private void AddCatchAllRule(string destination, RuleScope scope)
    {
        _rules.Add(new SortRule { Dest = destination, Scope = scope });
        SelectLastRule();
    }

    private void AddPresetRule(FilePreset preset)
    {
        _rules.Add(RuleFromPreset(preset));
        SelectLastRule();
    }

    private void SelectLastRule()
    {
        _selected = _rules.Count - 1;
        RebuildMaster();
        RebuildDetail();
    }

    private static SortRule RuleFromPreset(FilePreset preset) => new()
    {
        Dest = preset.Dest,
        All =
        {
            new RuleCondition
            {
                Field = ConditionField.Extension,
                Op = CompareOp.In,
                Value = preset.Extensions,
            },
        },
    };

    private void OnSaveAsPreset(SortRule rule)
    {
        var extensions = rule.All
            .FirstOrDefault(condition => condition.Field == ConditionField.Extension)?.Value;
        if (string.IsNullOrWhiteSpace(extensions))
        {
            DwMessageBox.Show(
                this,
                "Save as preset",
                "Presets are extension-based — add an Extension condition first.");
            return;
        }

        var prompt = new PromptWindow("Save as preset", "Preset name:") { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var name = prompt.Value.Trim();
        if (name.Length == 0) return;
        var presets = TargetStore.Config.Presets ??= PresetService.Defaults();
        presets.RemoveAll(preset => string.Equals(
            preset.Name,
            name,
            StringComparison.OrdinalIgnoreCase));
        presets.Add(PresetService.FromRule(name, rule)!);
        TargetStore.Save();
    }
}
