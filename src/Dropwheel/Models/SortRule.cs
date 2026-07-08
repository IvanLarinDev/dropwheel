namespace Dropwheel.Models;

/// <summary>Which file property a condition inspects.</summary>
public enum ConditionField { Extension, NameRegex, NameContains, SizeMb, AgeDays }

/// <summary>How a condition compares the file property against its value.</summary>
public enum CompareOp { In, Matches, Contains, Gt, Lt, Gte, Lte }

/// <summary>One test against a single file property. Value meaning depends on Field:
/// In — space-separated extensions ("jpg png"); SizeMb/AgeDays — a number; NameRegex — a pattern;
/// NameContains — a substring.</summary>
public sealed class RuleCondition
{
    public ConditionField Field { get; set; }
    public CompareOp Op { get; set; }
    public string Value { get; set; } = "";

    public RuleCondition Clone() => new()
    {
        Field = Field,
        Op = Op,
        Value = Value,
    };
}

/// <summary>A routing rule: send a file to Dest when all conditions match.
/// Dest is a subfolder relative to the sorter Path or an absolute path.
/// An empty All is a catch-all (matches every file). Order in the list is priority.</summary>
public sealed class SortRule
{
    public string Dest { get; set; } = "";
    public List<RuleCondition> All { get; set; } = new();

    public SortRule Clone() => new()
    {
        Dest = Dest,
        All = All.Select(c => c.Clone()).ToList(),
    };
}
