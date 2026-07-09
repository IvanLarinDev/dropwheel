using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public class AppConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_config_" + Guid.NewGuid().ToString("N"));

    public AppConfigTests()
    {
        Directory.CreateDirectory(_root);
        TargetStore.DirOverride = _root;
    }

    public void Dispose()
    {
        TargetStore.DirOverride = null;
        try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void Default_open_animation_preserves_existing_pop_behavior()
    {
        var config = new AppConfig();

        Assert.Equal(OpenAnimation.Pop, config.OpenAnimation);
    }

    [Fact]
    public void Default_open_animation_speed_is_normal()
    {
        var config = new AppConfig();

        Assert.Equal(1.0, config.OpenAnimationSpeed);
    }

    [Fact]
    public void OrderedForDisplay_without_positions_keeps_legacy_pinned_first_order()
    {
        var first = new TargetItem { Name = "first" };
        var pinned = new TargetItem { Name = "pinned", Pinned = true };
        var last = new TargetItem { Name = "last" };
        var ordered = TargetStore.OrderedForDisplay(new List<TargetItem> { first, pinned, last });

        Assert.Equal(new[] { "pinned", "first", "last" }, ordered.Select(t => t.Name));
    }

    [Fact]
    public void MoveTileBefore_persists_positions_through_config()
    {
        TargetStore.Config.Targets.Clear();
        var one = new TargetItem { Name = "one", Path = "C:\\one" };
        var two = new TargetItem { Name = "two", Path = "C:\\two" };
        var three = new TargetItem { Name = "three", Path = "C:\\three" };
        TargetStore.Config.Targets.AddRange(new[] { one, two, three });

        Assert.True(TargetStore.MoveTileBefore(TargetStore.Config.Targets, three, one));
        TargetStore.Save();
        TargetStore.Config.Targets.Clear();

        TargetStore.Load();

        Assert.Equal(new[] { "three", "one", "two" },
            TargetStore.OrderedForDisplay(TargetStore.Config.Targets).Select(t => t.Name));
        Assert.Equal(new int?[] { 0, 1, 2 }, TargetStore.Config.Targets.Select(t => t.TilePosition));
        Assert.Contains("\"TilePosition\"", File.ReadAllText(TargetStore.FilePath));
    }

    [Fact]
    public void MoveTileBefore_only_renumbers_the_current_level()
    {
        var root = new List<TargetItem>
        {
            new() { Name = "root" },
            new()
            {
                Name = "group",
                Children = new()
                {
                    new() { Name = "child-one" },
                    new() { Name = "child-two" },
                },
            },
        };
        var group = root[1];

        Assert.True(TargetStore.MoveTileBefore(group.Children!, group.Children![1], group.Children![0]));

        Assert.All(root, target => Assert.Null(target.TilePosition));
        Assert.Equal(new[] { "child-two", "child-one" }, group.Children!.Select(t => t.Name));
        Assert.Equal(new int?[] { 0, 1 }, group.Children!.Select(t => t.TilePosition));
    }

    [Fact]
    public void MoveToGroup_preserves_position_when_group_does_not_change()
    {
        TargetStore.Config.Targets.Clear();
        var item = new TargetItem { Name = "item", TilePosition = 2 };
        TargetStore.Config.Targets.Add(item);

        TargetStore.MoveToGroup(item, null);

        Assert.Same(item, Assert.Single(TargetStore.Config.Targets));
        Assert.Equal(2, item.TilePosition);
    }

    [Fact]
    public void MoveToGroup_clears_position_when_moving_between_levels()
    {
        TargetStore.Config.Targets.Clear();
        var item = new TargetItem { Name = "item", TilePosition = 2 };
        var group = new TargetItem { Name = "group", Children = new() };
        TargetStore.Config.Targets.AddRange(new[] { item, group });

        TargetStore.MoveToGroup(item, group);

        Assert.DoesNotContain(item, TargetStore.Config.Targets);
        Assert.Same(item, Assert.Single(group.Children!));
        Assert.Null(item.TilePosition);
    }

    [Fact]
    public void Bad_config_backup_path_is_timestamped_json()
    {
        var path = TargetStore.BackupPath(new DateTime(2026, 7, 8, 18, 30, 5));

        Assert.Equal(Path.Combine(_root, "config.bad.20260708_183005.json"), path);
    }

    [Fact]
    public void Load_backs_up_corrupt_config_before_recreating_defaults()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath, "{ not valid json");

        TargetStore.Load();

        var backup = Assert.Single(Directory.GetFiles(_root, "config.bad.*.json"));
        Assert.Equal("{ not valid json", File.ReadAllText(backup));
        Assert.NotEmpty(TargetStore.Config.Targets);
        Assert.Contains("\"Targets\"", File.ReadAllText(configPath));
    }

    [Fact]
    public void Load_does_not_overwrite_config_when_backup_fails()
    {
        var configPath = Path.Combine(_root, "config.json");
        const string original = "{ locked config";
        File.WriteAllText(configPath, original);

        using (new FileStream(configPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            TargetStore.Load();
            Assert.NotEmpty(TargetStore.Config.Targets);
        }

        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.Empty(Directory.GetFiles(_root, "config.bad.*.json"));
    }
}
