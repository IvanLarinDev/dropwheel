using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class LinkMetadataServiceTests
{
    [Fact]
    public void SourceUri_prefers_original_browser_url()
    {
        var target = new TargetItem
        {
            Path = "tg://privatepost?channel=1&post=2",
            SourceUrl = "https://t.me/c/1/2",
        };

        Assert.Equal("https://t.me/c/1/2", LinkMetadataService.SourceUri(target)?.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void ExtractTitle_decodes_and_compacts_page_title()
    {
        const string html = "<html><head><title> Example &amp;\n Docs </title></head></html>";

        Assert.Equal("Example & Docs", LinkMetadataService.ExtractTitle(html));
    }

    [Fact]
    public void ExtractIconUri_reads_relative_icon_link()
    {
        var page = new Uri("https://example.com/docs/page");
        const string html = "<html><head><link rel=\"shortcut icon\" href=\"/assets/favicon.png\"></head></html>";

        Assert.Equal("https://example.com/assets/favicon.png", LinkMetadataService.ExtractIconUri(page, html)?.AbsoluteUri);
    }

    [Fact]
    public void ExtractIconUri_falls_back_to_favicon_ico()
    {
        var page = new Uri("https://example.com/docs/page");

        Assert.Equal("https://example.com/favicon.ico", LinkMetadataService.ExtractIconUri(page, "<html></html>")?.AbsoluteUri);
    }

    [Fact]
    public void ExtractIconUri_skips_svg_icons()
    {
        var page = new Uri("https://example.com/docs/page");
        const string html = "<html><head><link rel=\"icon\" href=\"/icon.svg\"><link rel=\"icon\" href=\"/icon.png\"></head></html>";

        Assert.Equal("https://example.com/icon.png", LinkMetadataService.ExtractIconUri(page, html)?.AbsoluteUri);
    }
}
