using Dropwheel.Models;
using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OverlayBubbleTests
{
    [Fact]
    public void TileTooltip_shows_a_folder_targets_full_path()
    {
        var t = new TargetItem { Name = "Downloads", Path = @"C:\Users\me\Downloads" };
        Assert.Equal(@"C:\Users\me\Downloads", OverlayWindow.TileTooltip(t));
    }

    [Fact]
    public void TileTooltip_prefers_a_link_targets_url()
    {
        var t = new TargetItem { Name = "Site", Path = "https://x", SourceUrl = "https://example.com/page" };
        Assert.Equal("https://example.com/page", OverlayWindow.TileTooltip(t));
    }

    [Fact]
    public void TileTooltip_describes_a_group_instead_of_a_path()
    {
        var group = new TargetItem { Name = "Work", Children = new() { new TargetItem { Name = "A" } } };
        Assert.Equal("Group · 1 target(s)", OverlayWindow.TileTooltip(group));
    }

    [Fact]
    public void ParseTileColor_reads_hex_and_ignores_bad_or_empty_values()
    {
        Assert.NotNull(OverlayWindow.ParseTileColor("#4C8BF5"));
        Assert.Null(OverlayWindow.ParseTileColor(null));
        Assert.Null(OverlayWindow.ParseTileColor(""));
        Assert.Null(OverlayWindow.ParseTileColor("not a colour"));
    }
}
