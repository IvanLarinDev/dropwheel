using System.Windows;
using Dropwheel.Services;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;

namespace Dropwheel.Tests;

public sealed class LinkTargetServiceTests
{
    [Theory]
    [InlineData("tg://resolve?domain=telegram", "tg://resolve?domain=telegram", "Telegram: telegram")]
    [InlineData("Open https://t.me/telegram.", "https://t.me/telegram", "Telegram: telegram")]
    [InlineData("https://telegram.me/durov", "https://telegram.me/durov", "Telegram: durov")]
    [InlineData("https://t.me/+abcdef", "https://t.me/+abcdef", "Telegram invite")]
    public void CreateTarget_extracts_telegram_links(string text, string expectedPath, string expectedName)
    {
        var target = LinkTargetService.CreateTarget(text);

        Assert.NotNull(target);
        Assert.Equal(expectedPath, target.Path);
        Assert.Equal(expectedName, target.Name);
    }

    [Fact]
    public void CreateTarget_ignores_plain_text()
    {
        Assert.Null(LinkTargetService.CreateTarget("not a link"));
    }

    [Fact]
    public void CreateTarget_accepts_unicode_text_drop_data()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, "tg://resolve?domain=telegram");

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("tg://resolve?domain=telegram", target.Path);
        Assert.Equal("Telegram: telegram", target.Name);
    }

    [Fact]
    public void CreateTarget_accepts_browser_url_drop_format()
    {
        var data = new WpfDataObject();
        data.SetData("UniformResourceLocatorW", "https://t.me/telegram");

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("https://t.me/telegram", target.Path);
        Assert.Equal("Telegram: telegram", target.Name);
    }
}
