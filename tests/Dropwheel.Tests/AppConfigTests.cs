using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("TargetStoreState")]
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
    public void Default_group_shortcut_delay_is_400_ms()
    {
        Assert.Equal(400, new AppConfig().GroupShortcutDelayMs);
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
    public void MoveTileToEnd_moves_source_after_the_display_order()
    {
        var one = new TargetItem { Name = "one" };
        var two = new TargetItem { Name = "two" };
        var three = new TargetItem { Name = "three" };
        var targets = new List<TargetItem> { one, two, three };

        Assert.True(TargetStore.MoveTileToEnd(targets, one));

        Assert.Equal(new[] { "two", "three", "one" }, targets.Select(t => t.Name));
        Assert.Equal(new int?[] { 0, 1, 2 }, targets.Select(t => t.TilePosition));
    }

    [Fact]
    public void MoveTileToIndex_moves_forward_into_an_adjacent_tiles_slot()
    {
        var one = new TargetItem { Name = "one" };
        var executable = new TargetItem { Name = "executable", Path = "C:\\tool.exe" };
        var three = new TargetItem { Name = "three" };
        var targets = new List<TargetItem> { one, executable, three };

        Assert.True(TargetStore.MoveTileToIndex(targets, executable, 2));

        Assert.Equal(new[] { "one", "three", "executable" }, targets.Select(t => t.Name));
        Assert.Equal(new int?[] { 0, 1, 2 }, targets.Select(t => t.TilePosition));
    }

    [Fact]
    public void MoveTileToIndex_is_noop_for_the_current_slot()
    {
        var one = new TargetItem { Name = "one" };
        var two = new TargetItem { Name = "two" };
        var targets = new List<TargetItem> { one, two };

        Assert.False(TargetStore.MoveTileToIndex(targets, two, 1));

        Assert.Equal(new[] { "one", "two" }, targets.Select(t => t.Name));
        Assert.All(targets, target => Assert.Null(target.TilePosition));
    }

    [Fact]
    public void MoveTileToEnd_is_noop_when_source_is_already_last()
    {
        var one = new TargetItem { Name = "one" };
        var two = new TargetItem { Name = "two" };
        var targets = new List<TargetItem> { one, two };

        Assert.False(TargetStore.MoveTileToEnd(targets, two));

        Assert.Equal(new[] { "one", "two" }, targets.Select(t => t.Name));
        Assert.All(targets, target => Assert.Null(target.TilePosition));
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

    [Fact]
    public void Load_preserves_config_when_top_level_enum_token_is_unknown()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath,
            """
            {
              "GlobalAction": "Move",
              "OpenAnimation": "FutureSpin",
              "HoverDelayMs": 900,
              "Targets": [
                {
                  "Name": "Inbox",
                  "Path": "C:\\Temp\\Inbox",
                  "Override": "Copy",
                  "Pinned": true
                }
              ]
            }
            """);

        TargetStore.Load();

        Assert.Equal(DropAction.Move, TargetStore.Config.GlobalAction);
        Assert.Equal(OpenAnimation.Pop, TargetStore.Config.OpenAnimation);
        Assert.Equal(900, TargetStore.Config.HoverDelayMs);

        var target = Assert.Single(TargetStore.Config.Targets);
        Assert.Equal("Inbox", target.Name);
        Assert.Equal("C:\\Temp\\Inbox", target.Path);
        Assert.Equal(DropAction.Copy, target.Override);
        Assert.True(target.Pinned);

        var saved = File.ReadAllText(configPath);
        Assert.Contains("\"OpenAnimation\": \"Pop\"", saved);
        Assert.Contains("\"HoverDelayMs\": 900", saved);
        Assert.Contains("\"Name\": \"Inbox\"", saved);
    }

    [Fact]
    public void Load_preserves_config_when_only_the_overflow_layout_enum_is_unknown()
    {
        // Regression: OverflowLayout was missing from the enum sanitizer, so an unknown value here — with
        // every other enum valid — left `changed` false and threw, wiping the whole config to defaults.
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath,
            """
            {
              "OverflowLayout": "Spiral",
              "OverflowThreshold": 11,
              "Targets": [
                {
                  "Name": "Inbox",
                  "Path": "C:\\Temp\\Inbox",
                  "Pinned": true
                }
              ]
            }
            """);

        TargetStore.Load();

        Assert.Equal(OverflowLayout.None, TargetStore.Config.OverflowLayout);
        Assert.Equal(11, TargetStore.Config.OverflowThreshold);
        var target = Assert.Single(TargetStore.Config.Targets);
        Assert.Equal("Inbox", target.Name);
        Assert.Empty(Directory.GetFiles(_root, "config.bad.*.json"));
    }

    [Fact]
    public void Load_preserves_targets_when_target_override_enum_token_is_unknown()
    {
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath,
            """
            {
              "Targets": [
                {
                  "Name": "Archive",
                  "Path": "C:\\Temp\\Archive",
                  "Override": "Teleport",
                  "Pinned": true
                }
              ]
            }
            """);

        TargetStore.Load();

        var target = Assert.Single(TargetStore.Config.Targets);
        Assert.Equal("Archive", target.Name);
        Assert.Equal("C:\\Temp\\Archive", target.Path);
        Assert.Equal(DropAction.Inherit, target.Override);
        Assert.True(target.Pinned);

        var saved = File.ReadAllText(configPath);
        Assert.Contains("\"Override\": \"Inherit\"", saved);
        Assert.Contains("\"Name\": \"Archive\"", saved);
    }

    [Fact]
    public void Load_clamps_an_out_of_range_overflow_threshold()
    {
        File.WriteAllText(Path.Combine(_root, "config.json"),
            """
            { "OverflowThreshold": 999, "Targets": [] }
            """);

        TargetStore.Load();

        Assert.Equal(WheelLayout.MaxThreshold, TargetStore.Config.OverflowThreshold);
        Assert.NotEqual(999, TargetStore.Config.OverflowThreshold);
    }

    [Fact]
    public void DeleteTarget_removes_a_cached_favicon_under_the_icons_folder()
    {
        var iconsDir = Path.Combine(_root, "icons");
        Directory.CreateDirectory(iconsDir);
        var iconFile = Path.Combine(iconsDir, "abc.png");
        File.WriteAllBytes(iconFile, new byte[] { 1 });
        var link = new TargetItem { Name = "Link", Path = "https://example.com", IconPath = iconFile };
        TargetStore.Config.Targets.Clear();
        TargetStore.Config.Targets.Add(link);

        TargetStore.DeleteTarget(link);

        Assert.False(File.Exists(iconFile));
    }

    [Fact]
    public void DeleteTarget_keeps_a_favicon_still_used_by_another_target()
    {
        var iconsDir = Path.Combine(_root, "icons");
        Directory.CreateDirectory(iconsDir);
        var iconFile = Path.Combine(iconsDir, "shared.png");
        File.WriteAllBytes(iconFile, new byte[] { 1 });
        var one = new TargetItem { Name = "One", Path = "https://example.com", IconPath = iconFile };
        var two = new TargetItem { Name = "Two", Path = "https://example.com/2", IconPath = iconFile };
        TargetStore.Config.Targets.Clear();
        TargetStore.Config.Targets.Add(one);
        TargetStore.Config.Targets.Add(two);

        TargetStore.DeleteTarget(one);

        Assert.True(File.Exists(iconFile)); // still referenced by "two"
    }

    [Fact]
    public void DeleteTarget_leaves_a_custom_icon_outside_the_cache_untouched()
    {
        var custom = Path.Combine(_root, "my-custom.png");
        File.WriteAllBytes(custom, new byte[] { 1 });
        var t = new TargetItem { Name = "App", Path = @"C:\apps\tool.exe", IconPath = custom };
        TargetStore.Config.Targets.Clear();
        TargetStore.Config.Targets.Add(t);

        TargetStore.DeleteTarget(t);

        Assert.True(File.Exists(custom)); // user-chosen icon, not ours to delete
    }

    [Fact]
    public void Load_preserves_targets_when_a_rule_condition_enum_token_is_unknown()
    {
        // Regression: the sanitizer walked Override and Children but never a target's Rules, so a single
        // unknown ConditionField/CompareOp token left `changed` false, rethrew, and wiped the whole config.
        var configPath = Path.Combine(_root, "config.json");
        File.WriteAllText(configPath,
            """
            {
              "Targets": [
                {
                  "Name": "Sorter",
                  "Path": "C:\\Temp\\Sorter",
                  "Pinned": true,
                  "Rules": [
                    {
                      "Dest": "Images",
                      "All": [ { "Field": "Extension", "Op": "FutureOp", "Value": "jpg" } ]
                    }
                  ]
                }
              ]
            }
            """);

        TargetStore.Load();

        var target = Assert.Single(TargetStore.Config.Targets);
        Assert.Equal("Sorter", target.Name);
        Assert.True(target.Pinned);
        var rule = Assert.Single(target.Rules!);
        Assert.Equal("Images", rule.Dest);
        var condition = Assert.Single(rule.All);
        Assert.Equal(ConditionField.Extension, condition.Field);
        Assert.Equal(CompareOp.In, condition.Op); // unknown Op degraded to its default, not lost
        Assert.Equal("jpg", condition.Value);
        Assert.Empty(Directory.GetFiles(_root, "config.bad.*.json"));
    }

    [Fact]
    public void Load_assigns_stable_shortcuts_to_existing_groups_once()
    {
        File.WriteAllText(TargetStore.FilePath,
            """
            {
              "Targets": [
                { "Name": "One", "Path": "", "Children": [] },
                { "Name": "Two", "Path": "", "Children": [] }
              ]
            }
            """);

        TargetStore.Load();

        Assert.Equal(new[] { "1", "2" }, TargetStore.Groups.Select(group => group.GroupCode));
        Assert.True(TargetStore.Config.GroupShortcutsInitialized);

        TargetStore.Config.Targets[0].GroupCode = null;
        TargetStore.Save();
        TargetStore.Load();

        Assert.Null(TargetStore.Config.Targets[0].GroupCode);
        Assert.Equal("2", TargetStore.Config.Targets[1].GroupCode);
    }
}
