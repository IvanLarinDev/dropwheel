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
}
