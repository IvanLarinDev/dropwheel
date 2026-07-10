using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class TargetStoreDedupTests
{
    private static TargetItem Folder(string name, string path) => new() { Name = name, Path = path };

    [Fact]
    public void Same_path_differing_only_in_case_is_a_duplicate()
    {
        var level = new List<TargetItem> { Folder("Downloads", @"C:\Users\Ivan\Downloads") };
        var candidate = Folder("dl", @"c:\users\ivan\downloads");

        var (fresh, existing) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Empty(fresh);
        Assert.Single(existing);
        Assert.Same(level[0], existing[0]);
    }

    [Fact]
    public void Trailing_slash_and_forward_slashes_do_not_matter()
    {
        var level = new List<TargetItem> { Folder("Docs", @"D:\Docs") };
        var candidate = Folder("Docs", "D:/Docs/");

        var (fresh, _) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Empty(fresh);
    }

    [Fact]
    public void A_genuinely_new_path_is_kept()
    {
        var level = new List<TargetItem> { Folder("Docs", @"D:\Docs") };
        var candidate = Folder("Pics", @"D:\Pictures");

        var (fresh, existing) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Single(fresh);
        Assert.Same(candidate, fresh[0]);
        Assert.Empty(existing);
    }

    [Fact]
    public void Duplicates_within_one_batch_collapse_to_the_first()
    {
        var level = new List<TargetItem>();
        var first = Folder("A", @"D:\Shared");
        var second = Folder("A again", @"d:\shared\");

        var (fresh, existing) = TargetStore.SplitNewAndDuplicates(level, new[] { first, second });

        Assert.Single(fresh);
        Assert.Same(first, fresh[0]);
        Assert.Single(existing);
        Assert.Same(first, existing[0]);
    }

    [Fact]
    public void Link_targets_compare_as_strings_ignoring_case()
    {
        var level = new List<TargetItem> { new() { Name = "Site", Path = "https://Example.com/" } };
        var candidate = new TargetItem { Name = "Site", Path = "https://example.com/" };

        var (fresh, _) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Empty(fresh);
    }

    [Fact]
    public void Groups_are_never_treated_as_duplicates()
    {
        var level = new List<TargetItem> { new() { Name = "Work", Children = new() } };
        var candidate = new TargetItem { Name = "Work", Children = new() };

        var (fresh, existing) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Single(fresh);
        Assert.Empty(existing);
    }

    [Fact]
    public void Pathless_items_are_kept_as_new()
    {
        var level = new List<TargetItem> { Folder("Empty", "") };
        var candidate = Folder("Also empty", "   ");

        var (fresh, _) = TargetStore.SplitNewAndDuplicates(level, new[] { candidate });

        Assert.Single(fresh);
    }

    [Fact]
    public void A_partly_new_batch_keeps_new_items_in_order_and_reports_the_duplicate()
    {
        var existingTile = Folder("Docs", @"D:\Docs");
        var level = new List<TargetItem> { existingTile };
        var dup = Folder("Docs copy", @"D:\Docs");
        var a = Folder("A", @"D:\A");
        var b = Folder("B", @"D:\B");

        var (fresh, existing) = TargetStore.SplitNewAndDuplicates(level, new[] { a, dup, b });

        Assert.Equal(new[] { "A", "B" }, fresh.Select(t => t.Name).ToArray());
        Assert.Single(existing);
        Assert.Same(existingTile, existing[0]);
    }
}
