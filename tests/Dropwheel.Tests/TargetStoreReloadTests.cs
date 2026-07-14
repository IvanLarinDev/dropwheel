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
        try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { }
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
}
