using System.Text.RegularExpressions;
using System.Windows.Controls;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class TargetEditorWindow
{
    private static void MarkValue(TextBox box, RuleCondition condition)
    {
        var invalid = condition.Field == ConditionField.NameRegex && !IsValidRegex(condition.Value);
        box.BorderBrush = invalid ? Palettes.Danger : Palettes.Border;
        box.ToolTip = invalid ? "Invalid regular expression" : HintFor(condition.Field);
    }

    private static bool IsValidRegex(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        try { _ = new Regex(pattern); return true; }
        catch (RegexParseException) { return false; }
    }

    private bool TryValidateRules(out string error)
    {
        for (var i = 0; i < _rules.Count; i++)
        {
            foreach (var condition in _rules[i].All)
            {
                if (string.IsNullOrWhiteSpace(condition.Value))
                { error = $"Rule {i + 1}: a condition has an empty value."; return false; }
                if (condition.Field == ConditionField.NameRegex && !IsValidRegex(condition.Value))
                { error = $"Rule {i + 1}: invalid regular expression."; return false; }
                if (condition.Field is ConditionField.SizeMb or ConditionField.AgeDays or ConditionField.CreatedDaysAgo
                    && !double.TryParse(condition.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                { error = $"Rule {i + 1}: '{condition.Value}' is not a number."; return false; }
            }

            var available = SortService.AvailableTokens(_rules[i]);
            foreach (var (name, format) in SortService.ParseTokens(_rules[i].Dest))
            {
                if (!available.Contains(name) && !SortService.BuiltinTokens.Contains(name))
                { error = $"Rule {i + 1}: destination uses ${{{name}}} but no Name regex has a (?<{name}>…) group."; return false; }
                if (!SortService.TokenTakesFormat(name) || SortService.IsValidTokenFormat(name, format)) continue;
                error = name == "size"
                    ? $"Rule {i + 1}: '{format}' is not a valid size spec — use \"name limit, …, name\" with rising limits, e.g. \"tiny 0.5, small 10, huge\"."
                    : $"Rule {i + 1}: '{format}' is not a valid date format for ${{{name}}}.";
                return false;
            }
        }
        error = "";
        return true;
    }
}
