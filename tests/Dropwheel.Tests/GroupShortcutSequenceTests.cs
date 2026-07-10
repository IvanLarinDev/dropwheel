using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class GroupShortcutSequenceTests
{
    [Fact]
    public void Exact_code_without_longer_match_completes_immediately()
    {
        var sequence = Sequence("1", "23");

        var match = sequence.Push('1');

        Assert.Equal(GroupShortcutMatchKind.Exact, match.Kind);
        Assert.Equal("1", match.Input);
    }

    [Fact]
    public void Exact_code_that_is_also_a_prefix_waits_for_another_digit()
    {
        var sequence = Sequence("1", "11");

        var first = sequence.Push('1');
        var second = sequence.Push('1');

        Assert.Equal(GroupShortcutMatchKind.ExactWithLongerMatches, first.Kind);
        Assert.Equal(GroupShortcutMatchKind.Exact, second.Kind);
        Assert.Equal("11", second.Input);
    }

    [Fact]
    public void Timeout_chooses_an_ambiguous_exact_code()
    {
        var sequence = Sequence("1", "11");
        sequence.Push('1');

        var match = sequence.Timeout();

        Assert.Equal(GroupShortcutMatchKind.Exact, match.Kind);
        Assert.Equal("1", match.Input);
    }

    [Fact]
    public void Prefix_without_an_exact_code_expires_as_no_match()
    {
        var sequence = Sequence("23");

        Assert.Equal(GroupShortcutMatchKind.Partial, sequence.Push('2').Kind);
        Assert.Equal(GroupShortcutMatchKind.NoMatch, sequence.Timeout().Kind);
    }

    [Fact]
    public void Invalid_continuation_is_rejected_instead_of_falling_back_to_prefix()
    {
        var sequence = Sequence("1", "11");
        sequence.Push('1');

        var match = sequence.Push('2');

        Assert.Equal(GroupShortcutMatchKind.NoMatch, match.Kind);
        Assert.Equal("12", match.Input);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("99", true)]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("1a", false)]
    public void Code_validation_accepts_only_one_or_two_digits(string code, bool expected)
    {
        Assert.Equal(expected, GroupShortcutSequence.IsValidCode(code));
    }

    private static GroupShortcutSequence Sequence(params string[] codes)
    {
        var sequence = new GroupShortcutSequence();
        sequence.SetCodes(codes);
        return sequence;
    }
}
