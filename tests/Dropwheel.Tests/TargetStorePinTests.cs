using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class TargetStorePinTests
{
    private static TargetItem Target(string name) => new() { Name = name, Path = @"C:\" + name };

    private static string[] DisplayOrder(IList<TargetItem> targets) =>
        TargetStore.OrderedForDisplay(targets).Select(t => t.Name).ToArray();

    [Fact]
    public void PinToFront_sets_the_flag_and_moves_the_target_first()
    {
        var added = Target("New");
        var targets = new List<TargetItem> { Target("A"), Target("B"), added };

        TargetStore.PinToFront(targets, added);

        Assert.True(added.Pinned);
        Assert.Equal(new[] { "New", "A", "B" }, DisplayOrder(targets));
    }

    [Fact]
    public void PinToFront_wins_over_a_manual_tile_order()
    {
        var added = Target("New");
        var targets = new List<TargetItem> { Target("A"), Target("B"), added };
        TargetStore.RenumberTilePositions(targets);

        TargetStore.PinToFront(targets, added);

        Assert.Equal(new[] { "New", "A", "B" }, DisplayOrder(targets));
    }

    [Fact]
    public void Pinning_back_to_front_keeps_the_original_order_of_a_multi_file_drop()
    {
        var first = Target("First");
        var second = Target("Second");
        var targets = new List<TargetItem> { Target("Old"), first, second };

        foreach (var item in new[] { first, second }.Reverse())
            TargetStore.PinToFront(targets, item);

        Assert.Equal(new[] { "First", "Second", "Old" }, DisplayOrder(targets));
    }

    [Fact]
    public void An_already_pinned_first_target_stays_first()
    {
        var added = Target("New");
        var targets = new List<TargetItem> { added, Target("A") };

        TargetStore.PinToFront(targets, added);

        Assert.True(added.Pinned);
        Assert.Equal(new[] { "New", "A" }, DisplayOrder(targets));
    }
}
