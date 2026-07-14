namespace Dropwheel.Models;

/// <summary>Which file property a condition inspects.</summary>
public enum ConditionField { Extension, NameRegex, NameContains, SizeMb, AgeDays }

/// <summary>Which kinds of dropped item a rule is allowed to catch. Files is the default so old
/// configs and freshly added rules keep the original files-only behaviour; a rule must opt in to
/// touching folders.</summary>
public enum RuleScope { Files, Folders, Both }

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

    /// <summary>Inverts the condition: the rule treats it as met when the underlying test does NOT hold.
    /// Default false so old configs and new conditions keep the plain meaning.</summary>
    public bool Negate { get; set; }

    public RuleCondition Clone() => new()
    {
        Field = Field,
        Op = Op,
        Value = Value,
        Negate = Negate,
    };
}

/// <summary>A routing rule: send a file to Dest when all conditions match.
/// Dest is a subfolder relative to the sorter Path or an absolute path.
/// An empty All is a catch-all (matches every file). Order in the list is priority.</summary>
public sealed class SortRule
{
    public string Dest { get; set; } = "";
    public RuleScope Scope { get; set; } = RuleScope.Files;
    public List<RuleCondition> All { get; set; } = new();

    public SortRule Clone() => new()
    {
        Dest = Dest,
        Scope = Scope,
        All = All.Select(c => c.Clone()).ToList(),
    };
}
