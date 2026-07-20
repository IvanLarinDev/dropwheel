using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies the manual-edit reload path: a valid config.json replaces the in-memory config,
/// while a broken or missing file leaves the current settings exactly as they were and reports the
/// error — unlike startup Load, which would back the file up and reset to defaults.</summary>
[Collection("TargetStoreState")]
public sealed class TargetStoreReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_reload_" + Guid.NewGuid().ToString("N"));

    public TargetStoreReloadTests()
    {
        Directory.CreateDirectory(_root);
        TargetStore.DirOverride = _root;
    }

    public void Dispose()
    {
        TargetStore.DirOverride = null;
        TempDir.Delete(_root);
    }

    [Fact]
    public void Valid_file_replaces_the_config()
    {
        File.WriteAllText(TargetStore.FilePath, """{"HoverDelayMs": 777}""");

        Assert.True(TargetStore.TryReload(out var error));
        Assert.Equal("", error);
        Assert.Equal(777, TargetStore.Config.HoverDelayMs);
    }

    [Fact]
    public void Reloaded_config_gets_the_startup_fixups()
    {
        File.WriteAllText(TargetStore.FilePath, """{"OverflowThreshold": 999}""");

        Assert.True(TargetStore.TryReload(out _));

        Assert.NotNull(TargetStore.Config.Presets);
        Assert.InRange(TargetStore.Config.OverflowThreshold, 4, 16);
    }

    [Fact]
    public void Null_targets_are_normalized_before_the_config_is_published()
    {
        File.WriteAllText(TargetStore.FilePath, """{"HoverDelayMs": 888, "Targets": null}""");

        Assert.True(TargetStore.TryReload(out var error));

        Assert.Equal("", error);
        Assert.Equal(888, TargetStore.Config.HoverDelayMs);
        Assert.NotNull(TargetStore.Config.Targets);
        Assert.Empty(TargetStore.Config.Targets);
    }

    [Fact]
    public void Null_entries_are_removed_recursively_before_the_config_is_published()
    {
        File.WriteAllText(TargetStore.FilePath, """
            {
              "Targets": [
                null,
                {
                  "Name": "group",
                  "Children": [
                    null,
                    { "Name": "leaf", "Path": "x", "Rules": [null, { "Dest": null, "All": null }] }
                  ]
                }
              ]
            }
            """);

        Assert.True(TargetStore.TryReload(out var error));

        Assert.Equal("", error);
        var group = Assert.Single(TargetStore.Config.Targets);
        var leaf = Assert.Single(group.Children!);
        var rule = Assert.Single(leaf.Rules!);
        Assert.Equal("", rule.Dest);
        Assert.Empty(rule.All);
    }

    [Fact]
    public void Broken_file_keeps_current_settings_and_reports_the_error()
    {
        TargetStore.Load();
        TargetStore.Config.HoverDelayMs = 555;
        File.WriteAllText(TargetStore.FilePath, "{ not json at all");

        Assert.False(TargetStore.TryReload(out var error));

        Assert.NotEqual("", error);
        Assert.Equal(555, TargetStore.Config.HoverDelayMs);
        Assert.Equal("{ not json at all", File.ReadAllText(TargetStore.FilePath));
    }

    [Fact]
    public void Broken_file_is_not_backed_up_or_replaced()
    {
        File.WriteAllText(TargetStore.FilePath, "broken");

        Assert.False(TargetStore.TryReload(out _));

        Assert.Empty(Directory.GetFiles(_root, "config.bad.*.json"));
    }

    [Fact]
    public void Missing_file_reports_the_error_and_keeps_settings()
    {
        TargetStore.Load();
        TargetStore.Config.HoverDelayMs = 444;
        File.Delete(TargetStore.FilePath);

        Assert.False(TargetStore.TryReload(out var error));

        Assert.Contains("not found", error);
        Assert.Equal(444, TargetStore.Config.HoverDelayMs);
    }

    [Fact]
    public async Task Failed_replacement_write_keeps_the_live_config_unchanged()
    {
        TargetStore.Load();
        var live = TargetStore.Config;
        var draft = TargetStore.CloneConfig(live);
        draft.HoverDelayMs = 999;
        var fileInsteadOfDirectory = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "occupied");
        TargetStore.DirOverride = fileInsteadOfDirectory;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => TargetStore.ReplaceAndSaveAsync(draft));
            Assert.Same(live, TargetStore.Config);
            Assert.NotEqual(999, TargetStore.Config.HoverDelayMs);
        }
        finally { TargetStore.DirOverride = _root; }
    }
}
