using System.IO;
using System.Reflection;
using System.Windows.Threading;
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
        Assert.True(WatcherService.SameFolder(folder, file)); // stays in its own folder - don't move
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
        Assert.False(WatcherService.SameFolder(folder, file)); // routed into a subfolder - move it
    }

    [Fact]
    public void Auto_sort_skips_move_when_destination_already_has_a_conflicting_file()
    {
        var file = Path.Combine(_root, "a.jpg");
        var destFolder = Path.Combine(_root, "Images");
        Directory.CreateDirectory(destFolder);
        File.WriteAllText(file, "source");
        File.WriteAllText(Path.Combine(destFolder, "a.jpg"), "existing");

        var target = new TargetItem
        {
            Path = _root,
            Rules = new() { new SortRule { Dest = "Images", All = { new RuleCondition
                { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } } } },
        };

        var service = new WatcherService(Dispatcher.CurrentDispatcher, _ => { });
        InvokeSortOne(service, target, file);

        Assert.True(File.Exists(file));
        Assert.Equal("source", File.ReadAllText(file));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destFolder, "a.jpg")));
    }

    [Fact]
    public async Task Wait_until_ready_returns_false_when_cancelled_before_file_is_ready()
    {
        var file = Path.Combine(_root, "locked.mov");
        File.WriteAllBytes(file, Array.Empty<byte>());

        using var cts = new CancellationTokenSource();
        var waitTask = WatcherService.WaitUntilReadyAsync(
            file,
            _ => false,
            pollMs: 1,
            maxWaitTicks: 1000,
            cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(10));

        Assert.False(await waitTask);
    }

    [Fact]
    public async Task Wait_until_ready_checks_cancellation_before_readiness()
    {
        var file = Path.Combine(_root, "ready.mov");
        File.WriteAllBytes(file, Array.Empty<byte>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ready = await WatcherService.WaitUntilReadyAsync(
            file,
            _ => true,
            pollMs: 1,
            maxWaitTicks: 1,
            cts.Token);

        Assert.False(ready);
    }

    private static void InvokeSortOne(WatcherService service, TargetItem target, string file)
    {
        var entryType = typeof(WatcherService).GetNestedType("Entry", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WatcherService.Entry not found.");
        var entry = Activator.CreateInstance(entryType, nonPublic: true)
            ?? throw new InvalidOperationException("WatcherService.Entry could not be created.");
        entryType.GetProperty("Target", BindingFlags.Instance | BindingFlags.Public)?.SetValue(entry, target);

        var sortOne = typeof(WatcherService).GetMethod("SortOne", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WatcherService.SortOne not found.");
        sortOne.Invoke(service, new[] { entry, file, CancellationToken.None });
    }

    [Fact]
    public void MovePlan_skips_files_that_would_stay_in_their_own_folder()
    {
        var stay = Path.Combine(_root, "a.xyz");
        var move = Path.Combine(_root, "a.jpg");
        File.WriteAllBytes(stay, Array.Empty<byte>());
        File.WriteAllBytes(move, Array.Empty<byte>());
        var t = new TargetItem
        {
            Path = _root,
            Rules = new() { new SortRule { Dest = "Images", All = { new RuleCondition
                { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } } } },
        };

        var plan = SortService.MovePlan(t, new[] { stay, move });

        var group = Assert.Single(plan);
        Assert.Equal(Path.Combine(_root, "Images"), group.Key);
        Assert.Equal(new[] { move }, group.Value);
    }
}
