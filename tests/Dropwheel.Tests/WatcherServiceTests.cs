using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies the pure decision the folder watcher relies on: a file whose destination is its
/// own folder must be left in place (no move, no loop), while a file routed into a subfolder moves.
/// The FileSystemWatcher timing itself is covered by live testing, not here.</summary>
public sealed class WatcherServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_watch_" + Guid.NewGuid().ToString("N"));

    public WatcherServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void SameFolder_true_when_destination_is_the_files_own_folder()
    {
        var file = Path.Combine(_root, "a.mov");
        Assert.True(WatcherService.SameFolder(_root, file));
    }

    [Fact]
    public void SameFolder_ignores_trailing_separator_and_case()
    {
        var file = Path.Combine(_root, "a.mov");
        Assert.True(WatcherService.SameFolder(_root + Path.DirectorySeparatorChar, file));
        Assert.True(WatcherService.SameFolder(_root.ToUpperInvariant(), file));
    }

    [Fact]
    public void SameFolder_false_for_a_subfolder()
    {
        var file = Path.Combine(_root, "a.mov");
        Assert.False(WatcherService.SameFolder(Path.Combine(_root, "Images"), file));
    }

    [Fact]
    public void No_match_resolves_to_own_folder_so_the_file_is_left_in_place()
    {
        var file = Path.Combine(_root, "a.xyz");
        File.WriteAllBytes(file, Array.Empty<byte>());
        var t = new TargetItem
        {
            Path = _root,
            Rules = new() { new SortRule { Dest = "Images", All = { new RuleCondition
                { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } } } },
        };
        var folder = SortService.Plan(t, new[] { file }).Keys.Single();
        Assert.True(WatcherService.SameFolder(folder, file)); // stays in its own folder — don't move
    }

    [Fact]
    public void Matching_rule_routes_into_a_subfolder_so_the_file_moves()
    {
        var file = Path.Combine(_root, "a.jpg");
        File.WriteAllBytes(file, Array.Empty<byte>());
        var t = new TargetItem
        {
            Path = _root,
            Rules = new() { new SortRule { Dest = "Images", All = { new RuleCondition
                { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } } } },
        };
        var folder = SortService.Plan(t, new[] { file }).Keys.Single();
        Assert.False(WatcherService.SameFolder(folder, file)); // routed into a subfolder — move it
    }
}
