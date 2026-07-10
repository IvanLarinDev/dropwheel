using Dropwheel.Models;
using Dropwheel.Services;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlayAddUndoTests
{
    private static TargetItem Target(string name) => new() { Name = name, Path = @"C:\" + name };

    private static OverlayWindow.AddOp Snapshot(IList<TargetItem> level)
        => new(level, level.Select(t => (t, t.TilePosition)).ToList());

    private static string[] Display(IList<TargetItem> level)
        => TargetStore.OrderedForDisplay(level).Select(t => t.Name).ToArray();

    [Fact]
    public void Restoring_drops_a_plainly_added_target()
    {
        var level = new List<TargetItem> { Target("A"), Target("B") };
        var snapshot = Snapshot(level);

        level.Add(Target("New"));
        OverlayWindow.RestoreLevel(snapshot);

        Assert.Equal(new[] { "A", "B" }, Display(level));
    }

    [Fact]
    public void Restoring_drops_a_pinned_target_and_brings_back_the_old_order()
    {
        var level = new List<TargetItem> { Target("A"), Target("B") };
        var snapshot = Snapshot(level);

        var added = Target("New");
        level.Add(added);
        TargetStore.PinToFront(level, added);

        Assert.Equal(new[] { "New", "A", "B" }, Display(level));

        OverlayWindow.RestoreLevel(snapshot);

        Assert.Equal(new[] { "A", "B" }, Display(level));
        Assert.All(level, t => Assert.Null(t.TilePosition));
    }

    [Fact]
    public void Restoring_keeps_a_manual_tile_order_intact()
    {
        var level = new List<TargetItem> { Target("A"), Target("B"), Target("C") };
        TargetStore.RenumberTilePositions(level);
        var snapshot = Snapshot(level);

        var added = Target("New");
        level.Add(added);
        TargetStore.PinToFront(level, added);

        OverlayWindow.RestoreLevel(snapshot);

        Assert.Equal(new[] { "A", "B", "C" }, Display(level));
        Assert.Equal(new int?[] { 0, 1, 2 }, level.Select(t => t.TilePosition).ToArray());
    }
}
