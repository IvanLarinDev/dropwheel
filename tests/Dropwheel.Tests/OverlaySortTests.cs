using System.IO;
using Dropwheel.Models;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlaySortTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_sort_overlay_" + Guid.NewGuid().ToString("N"));

    public OverlaySortTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void ExecutableSorterGroups_skips_sources_already_in_destination_folder()
    {
        var inRoot = Path.Combine(_root, "already-here.txt");
        var incomingDir = Path.Combine(_root, "incoming");
        var incoming = Path.Combine(incomingDir, "move-me.txt");
        Directory.CreateDirectory(incomingDir);
        File.WriteAllText(inRoot, "same-folder");
        File.WriteAllText(incoming, "incoming");
        var plan = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [_root] = new() { inRoot, incoming },
        };

        var groups = OverlayWindow.ExecutableSorterGroups(plan);

        var group = Assert.Single(groups);
        Assert.Equal(_root, group.Folder);
        Assert.Equal(new[] { incoming }, group.Sources);
    }

    [Fact]
    public void SameNormalizedFolder_ignores_trailing_separator_and_case()
    {
        var canonical = _root;
        var variant = canonical.ToUpperInvariant() + Path.DirectorySeparatorChar;

        Assert.True(OverlayWindow.SameNormalizedFolder(canonical, variant));
    }

    [Fact]
    public void Skip_conflict_policy_filters_only_colliding_sorter_sources()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var colliding = Path.Combine(source, "same.txt");
        var fresh = Path.Combine(source, "new.txt");
        File.WriteAllText(colliding, "new");
        File.WriteAllText(fresh, "fresh");
        File.WriteAllText(Path.Combine(destination, "same.txt"), "old");

        Assert.Equal(
            new[] { fresh },
            OverlayWindow.ApplyConflictPolicy(
                new[] { colliding, fresh }, destination, ConflictPolicy.Skip));
        Assert.Equal(
            new[] { colliding, fresh },
            OverlayWindow.ApplyConflictPolicy(
                new[] { colliding, fresh }, destination, ConflictPolicy.KeepBoth));
    }

    [Fact]
    public void Non_keep_both_policies_accept_only_one_source_per_batch_destination()
    {
        var firstFolder = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var secondFolder = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var first = Path.Combine(firstFolder, "same.txt");
        var second = Path.Combine(secondFolder, "same.txt");

        Assert.Equal(
            new[] { first },
            OverlayWindow.ApplyConflictPolicy(
                new[] { first, second }, destination, ConflictPolicy.Overwrite));
        Assert.Equal(
            new[] { first, second },
            OverlayWindow.ApplyConflictPolicy(
                new[] { first, second }, destination, ConflictPolicy.KeepBoth));
        Assert.Contains("1 skipped", OverlayWindow.SortedToastText(1, "Sorter", 1, skipped: 1));
    }

    [Fact]
    public void Named_batch_policy_counts_duplicate_destinations_as_skipped()
    {
        var destination = Path.Combine(_root, "archive.txt");
        var pairs = new[]
        {
            (Source: Path.Combine(_root, "a.txt"), Dest: destination),
            (Source: Path.Combine(_root, "b.txt"), Dest: destination),
        };

        var accepted = OverlayWindow.ApplyBatchDestinationPolicy(
            pairs,
            ConflictPolicy.Ask,
            out var skipped);

        Assert.Single(accepted);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void Highlight_bounds_keep_ui_automation_coordinates_in_physical_pixels()
    {
        var bounds = OverlayWindow.HighlightBounds(new System.Windows.Rect(100.25, 200.75, 50.5, 20.25));

        Assert.Equal(new OverlayWindow.PhysicalWindowBounds(97, 197, 57, 27), bounds);
    }
}
