using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class HintPolicyTests
{
    [Fact]
    public void Allows_a_hint_up_to_its_cap_then_stops()
    {
        var shows = new Dictionary<string, int>();

        Assert.True(HintPolicy.RecordAndAllow(shows, "tip", 3));
        Assert.True(HintPolicy.RecordAndAllow(shows, "tip", 3));
        Assert.True(HintPolicy.RecordAndAllow(shows, "tip", 3));
        Assert.False(HintPolicy.RecordAndAllow(shows, "tip", 3));

        Assert.Equal(3, shows["tip"]);
    }

    [Fact]
    public void A_maxed_out_hint_is_not_counted_further()
    {
        var shows = new Dictionary<string, int> { ["tip"] = 5 };

        Assert.False(HintPolicy.RecordAndAllow(shows, "tip", 1));

        Assert.Equal(5, shows["tip"]);
    }

    [Fact]
    public void Different_hints_are_counted_independently()
    {
        var shows = new Dictionary<string, int>();

        Assert.True(HintPolicy.RecordAndAllow(shows, "a", 1));
        Assert.False(HintPolicy.RecordAndAllow(shows, "a", 1));
        Assert.True(HintPolicy.RecordAndAllow(shows, "b", 1));
    }
}
