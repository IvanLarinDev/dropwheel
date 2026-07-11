using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Covers icon-path selection and the cache's handling of a failed lookup.</summary>
public sealed class IconServiceTests
{
    [Fact]
    public void IconLookupPath_prefers_an_existing_IconPath()
    {
        var iconFile = Path.Combine(Path.GetTempPath(), "dw_icon_" + Guid.NewGuid().ToString("N") + ".ico");
        File.WriteAllBytes(iconFile, new byte[] { 0 });
        try
        {
            var target = new TargetItem { Name = "x", Path = @"C:\apps\tool.exe", IconPath = iconFile };
            Assert.Equal(iconFile, IconService.IconLookupPath(target));
        }
        finally { File.Delete(iconFile); }
    }

    [Fact]
    public void IconLookupPath_falls_back_to_target_path_when_IconPath_is_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dw_missing_" + Guid.NewGuid().ToString("N") + ".ico");
        var target = new TargetItem { Name = "x", Path = @"C:\apps\tool.exe", IconPath = missing };

        Assert.Equal(@"C:\apps\tool.exe", IconService.IconLookupPath(target));
    }

    [Fact]
    public void IconLookupPath_uses_target_path_when_no_IconPath_set()
    {
        var target = new TargetItem { Name = "x", Path = @"C:\apps\tool.exe" };

        Assert.Equal(@"C:\apps\tool.exe", IconService.IconLookupPath(target));
    }

    [Fact]
    public void GetIcon_does_not_cache_a_failed_lookup()
    {
        // A path with no loadable image and no shell icon returns null; it must NOT be cached, so a
        // later attempt (once the file appears or unlocks) can still succeed instead of being frozen.
        var path = Path.Combine(Path.GetTempPath(), "dw_no_icon_" + Guid.NewGuid().ToString("N") + ".zzz");

        Assert.Null(IconService.GetIcon(path));
        Assert.False(IconService.IsCached(path));
    }
}
