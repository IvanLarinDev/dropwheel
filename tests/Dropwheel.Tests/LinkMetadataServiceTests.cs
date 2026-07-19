using System.IO;
using System.Net;
using System.Net.Http;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("TargetStoreState")]
public sealed class LinkMetadataServiceTests
{
    [Fact]
    public void CachedIconPath_returns_a_seeded_file_for_an_extensioned_url()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_icons_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        TargetStore.DirOverride = root;
        try
        {
            var iconUri = new Uri("https://example.com/assets/favicon.png");
            Assert.Null(LinkMetadataService.CachedIconPath(iconUri)); // nothing cached yet

            var path = LinkMetadataService.IconCachePathCandidate(iconUri)!;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.Equal(path, LinkMetadataService.CachedIconPath(iconUri)); // cache hit, no network needed
        }
        finally
        {
            TargetStore.DirOverride = null;
            TempDir.Delete(root);
        }
    }

    [Fact]
    public void CachedIconPath_is_null_for_an_extensionless_url()
    {
        // Without a usable extension the file name depends on the server Content-Type, so the cache path
        // can't be known up front and a fetch is still required.
        Assert.Null(LinkMetadataService.CachedIconPath(new Uri("https://example.com/icon")));
    }

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

    [Theory]
    [InlineData("93.184.216.34", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    public void Network_policy_allows_only_public_addresses(string address, bool expected) =>
        Assert.Equal(expected, LinkMetadataNetworkPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task Network_transport_rechecks_dns_and_refuses_a_rebound_private_address()
    {
        var policy = new LinkMetadataNetworkPolicy((_, _) =>
            Task.FromResult(new[] { IPAddress.Loopback }));

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await policy.ConnectAsync(new DnsEndPoint("example.com", 80), CancellationToken.None));
    }

    [Fact]
    public async Task Metadata_request_blocks_redirect_to_loopback_before_second_request()
    {
        var handler = new RedirectHandler(new Uri("http://127.0.0.1/admin"));
        using var client = new HttpClient(handler);
        var policy = new LinkMetadataNetworkPolicy((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LinkMetadataService.GetAllowedAsync(client, new Uri("https://example.com"), policy, CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class RedirectHandler(Uri location) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = location },
                RequestMessage = request,
            });
        }
    }
}
