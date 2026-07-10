using System.Windows;
using Dropwheel.Services;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;

namespace Dropwheel.Tests;

public sealed class LinkTargetServiceTests
{
    [Theory]
    [InlineData("tg://resolve?domain=telegram", "tg://resolve?domain=telegram", "Telegram: telegram")]
    [InlineData("Open https://t.me/telegram.", "tg://resolve?domain=telegram", "Telegram: telegram")]
    [InlineData("t.me/c/2669588230/1", "tg://privatepost?channel=2669588230&post=1", "Telegram topic")]
    [InlineData("t.me/c/2669588230/1/2", "tg://privatepost?channel=2669588230&topic=1&post=2", "Telegram topic")]
    [InlineData("https://telegram.me/durov", "tg://resolve?domain=durov", "Telegram: durov")]
    [InlineData("https://t.me/group/1/2", "tg://resolve?domain=group&topic=1&post=2", "Telegram: group")]
    [InlineData("durov.t.me", "tg://resolve?domain=durov", "Telegram")]
    [InlineData("https://t.me/+abcdef", "tg://join?invite=abcdef", "Telegram invite")]
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

    [Theory]
    [InlineData("Saved Messages")]
    [InlineData("Saved message")]
    [InlineData("Избранное")]
    public void HasSavedMessagesLabel_detects_saved_messages_chat(string text)
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, text);

        Assert.True(LinkTargetService.HasSavedMessagesLabel(data));
    }

    [Theory]
    [InlineData("@durov", "tg://resolve?domain=durov")]
    [InlineData("durov", "tg://resolve?domain=durov")]
    [InlineData("+15555550123", "tg://resolve?phone=%2B15555550123")]
    [InlineData("https://t.me/telegram", "tg://resolve?domain=telegram")]
    public void CreateSavedMessagesTarget_builds_self_chat_target(string account, string expectedPath)
    {
        var target = LinkTargetService.CreateSavedMessagesTarget(account);

        Assert.NotNull(target);
        Assert.Equal("Saved Messages", target.Name);
        Assert.Equal(expectedPath, target.Path);
    }

    [Fact]
    public void CreateTarget_accepts_unicode_text_drop_data()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, "t.me/c/2669588230/1");

        Assert.True(LinkTargetService.HasPotentialLaunchUriData(data));

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("tg://privatepost?channel=2669588230&post=1", target.Path);
        Assert.Equal("Telegram topic", target.Name);
    }

    [Fact]
    public void CreateTarget_accepts_browser_url_drop_format()
    {
        var data = new WpfDataObject();
        data.SetData("UniformResourceLocatorW", "https://t.me/telegram");

        Assert.True(LinkTargetService.HasPotentialLaunchUriData(data));

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("tg://resolve?domain=telegram", target.Path);
        Assert.Equal("Telegram: telegram", target.Name);
    }

    [Fact]
    public void CreateTarget_uses_browser_drop_title_when_available()
    {
        var data = new WpfDataObject();
        data.SetData("text/x-moz-url", "https://example.com/article\r\nReadable Article");

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("https://example.com/article", target.Path);
        Assert.Equal("https://example.com/article", target.SourceUrl);
        Assert.Equal("Readable Article", target.Name);
    }

    [Fact]
    public void CreateTarget_keeps_source_url_for_telegram_web_links()
    {
        var data = new WpfDataObject();
        data.SetData("text/x-moz-url", "https://t.me/c/2669588230/1\r\nGeneral");

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("tg://privatepost?channel=2669588230&post=1", target.Path);
        Assert.Equal("https://t.me/c/2669588230/1", target.SourceUrl);
        Assert.Equal("General", target.Name);
    }

    [Fact]
    public void CreateTarget_uses_html_anchor_text_when_title_is_missing()
    {
        const string html = "Version:0.9\r\n<html><body><!--StartFragment--><a href=\"https://example.com/x\">Example &amp; Docs</a><!--EndFragment--></body></html>";
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.Html, html);

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("https://example.com/x", target.Path);
        Assert.Equal("Example & Docs", target.Name);
    }

    [Fact]
    public void CreateTarget_prefers_titled_browser_candidate_over_url_only_candidate()
    {
        const string html = "Version:0.9\r\n<html><body><!--StartFragment--><a href=\"https://example.com/x\">Example Docs</a><!--EndFragment--></body></html>";
        var data = new WpfDataObject();
        data.SetData("UniformResourceLocatorW", "https://example.com/x");
        data.SetData(WpfDataFormats.Html, html);

        var target = LinkTargetService.CreateTarget(data);

        Assert.NotNull(target);
        Assert.Equal("https://example.com/x", target.Path);
        Assert.Equal("Example Docs", target.Name);
    }
}
