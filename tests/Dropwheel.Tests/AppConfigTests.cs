using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public class AppConfigTests
{
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

        Assert.EndsWith(Path.Combine("Dropwheel", "config.bad.20260708_183005.json"), path);
    }
}
