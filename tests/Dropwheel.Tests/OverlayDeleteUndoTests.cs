using Dropwheel.Models;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlayDeleteUndoTests
{
    private static TargetItem Target(string name) => new() { Name = name, Path = @"C:\" + name };

    private static string[] Names(IList<TargetItem> level) => level.Select(t => t.Name).ToArray();

    [Fact]
    public void Restoring_puts_a_deleted_target_back_at_its_index()
    {
        var a = Target("A");
        var b = Target("B");
        var c = Target("C");
        var level = new List<TargetItem> { a, b, c };

        level.RemoveAt(1); // delete B
        OverlayWindow.RestoreDelete(new OverlayWindow.DeleteOp(level, b, 1));

        Assert.Equal(new[] { "A", "B", "C" }, Names(level));
    }

    [Fact]
    public void Restoring_the_first_target_keeps_it_first()
    {
        var a = Target("A");
        var level = new List<TargetItem> { a, Target("B") };

        level.RemoveAt(0);
        OverlayWindow.RestoreDelete(new OverlayWindow.DeleteOp(level, a, 0));

        Assert.Equal(new[] { "A", "B" }, Names(level));
    }

    [Fact]
    public void A_stale_index_past_the_end_clamps_to_the_tail_instead_of_throwing()
    {
        var x = Target("X");
        var level = new List<TargetItem> { Target("A") };

        OverlayWindow.RestoreDelete(new OverlayWindow.DeleteOp(level, x, 5));

        Assert.Equal(new[] { "A", "X" }, Names(level));
    }
}
