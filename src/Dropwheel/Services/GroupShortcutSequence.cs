namespace Dropwheel.Services;

public enum GroupShortcutMatchKind
{
    NoMatch,
    Partial,
    Exact,
    ExactWithLongerMatches,
}

public readonly record struct GroupShortcutMatch(GroupShortcutMatchKind Kind, string Input);

/// <summary>Pure state machine for one- and two-digit group codes. The UI owns the timer; this
/// type only determines whether an input is complete, ambiguous, or invalid.</summary>
public sealed class GroupShortcutSequence
{
    private string[] _codes = [];

    public string Input { get; private set; } = "";

    public static bool IsValidCode(string? code) =>
        code is { Length: >= 1 and <= 2 } && code.All(char.IsAsciiDigit);

    public void SetCodes(IEnumerable<string?> codes)
    {
        _codes = codes
            .Where(IsValidCode)
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Reset();
    }

    public GroupShortcutMatch Push(char digit)
    {
        if (!char.IsAsciiDigit(digit))
            throw new ArgumentOutOfRangeException(nameof(digit), "A shortcut digit must be 0-9.");

        Input += digit;
        return Evaluate();
    }

    public GroupShortcutMatch Timeout()
    {
        var exact = _codes.Contains(Input, StringComparer.Ordinal);
        return new GroupShortcutMatch(
            exact ? GroupShortcutMatchKind.Exact : GroupShortcutMatchKind.NoMatch,
            Input);
    }

    public void Reset() => Input = "";

    private GroupShortcutMatch Evaluate()
    {
        var exact = false;
        var longer = false;
        foreach (var code in _codes)
        {
            if (code == Input) exact = true;
            else if (code.StartsWith(Input, StringComparison.Ordinal)) longer = true;
        }

        var kind = (exact, longer) switch
        {
            (true, true) => GroupShortcutMatchKind.ExactWithLongerMatches,
            (true, false) => GroupShortcutMatchKind.Exact,
            (false, true) => GroupShortcutMatchKind.Partial,
            _ => GroupShortcutMatchKind.NoMatch,
        };
        return new GroupShortcutMatch(kind, Input);
    }
}
