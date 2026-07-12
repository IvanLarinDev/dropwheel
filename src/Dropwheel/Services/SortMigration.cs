using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Converts legacy SortRules into the rich Rules model, preserving order.</summary>
public static class SortMigration
{
    /// <summary>"jpg png":"Images" → Rule{Dest=Images, All=[Extension In "jpg png"]};
    /// "*":"Other" → Rule{Dest=Other, All=[]}. Catch-all rules are moved to the end so the
    /// first-match-wins semantics stay intact.</summary>
    public static List<SortRule> ToRules(IDictionary<string, string> sortRules)
    {
        var rules = new List<SortRule>();
        var catchAll = new List<SortRule>();
        foreach (var (key, dest) in sortRules)
        {
            if (key.Trim() == "*")
                catchAll.Add(new SortRule { Dest = dest });
            else
                rules.Add(new SortRule
                {
                    Dest = dest,
                    All = { new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In, Value = key } },
                });
        }
        rules.AddRange(catchAll);
        return rules;
    }

    /// <summary>Migrates a target in place. No-op if it already has Rules or has no legacy rules.
    /// Otherwise fills Rules from SortRules and clears SortRules. Returns whether anything changed.</summary>
    public static bool Migrate(TargetItem t)
    {
        if (t.Rules is { Count: > 0 } || t.SortRules is not { Count: > 0 }) return false;
        t.Rules = ToRules(t.SortRules);
        t.SortRules = null;
        return true;
    }
}
