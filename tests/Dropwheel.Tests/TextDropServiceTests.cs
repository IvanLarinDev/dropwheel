using System.IO;
using System.Text;
using Dropwheel.Services;
using WpfDataObject = System.Windows.DataObject;
using WpfDataFormats = System.Windows.DataFormats;

namespace Dropwheel.Tests;

/// <summary>Verifies text-drop extension detection and file creation (name, content, collisions).</summary>
public sealed class TextDropServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_text_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime When = new(2026, 7, 6, 23, 15, 4);

    public TextDropServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Theory]
    [InlineData("# Heading\n\ntext", "md")]
    [InlineData("see ```code``` here", "md")]
    [InlineData("a [link](https://x.com) inline", "md")]
    [InlineData("just a plain sentence", "txt")]
    [InlineData("line one\nline two", "txt")]
    public void ExtensionFor_detects_markdown(string text, string expected) =>
        Assert.Equal(expected, TextDropService.ExtensionFor(text));

    [Fact]
    public void Hash_in_the_middle_is_not_a_heading()
    {
        Assert.False(TextDropService.LooksLikeMarkdown("issue #42 was fixed"));
        Assert.Equal("txt", TextDropService.ExtensionFor("issue #42 was fixed"));
    }

    [Fact]
    public void GetText_reads_string_format()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.StringFormat, "from editor");

        Assert.True(TextDropService.HasText(data));
        Assert.Equal("from editor", TextDropService.GetText(data));
    }

    [Fact]
    public void GetText_reads_oem_text_format()
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.OemText, "from editor");

        Assert.True(TextDropService.HasText(data));
        Assert.Equal("from editor", TextDropService.GetText(data));
    }

    [Fact]
    public void GetText_reads_utf8_memory_stream_plain_text()
    {
        var data = new WpfDataObject();
        data.SetData("text/plain", new MemoryStream(Encoding.UTF8.GetBytes("stream text\0")));

        Assert.Equal("stream text", TextDropService.GetText(data));
    }

    [Fact]
    public void GetText_reads_html_fragment_when_plain_text_is_missing()
    {
        const string html = "Version:0.9\r\nStartHTML:00000097\r\nEndHTML:00000165\r\nStartFragment:00000129\r\nEndFragment:00000133\r\n<html><body><!--StartFragment-->hi<br>there<!--EndFragment--></body></html>";
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.Html, html);

        Assert.Equal("hi\nthere", TextDropService.GetText(data));
    }

    [Fact]
    public void Save_writes_content_with_timestamped_name()
    {
        var path = TextDropService.Save("hello world", _root, When);
        Assert.Equal("text_2026-07-06_23-15-04.txt", Path.GetFileName(path));
        Assert.Equal("hello world", File.ReadAllText(path));
    }

    [Fact]
    public void Save_markdown_uses_md_extension()
    {
        var path = TextDropService.Save("# Title\nbody", _root, When);
        Assert.Equal(".md", Path.GetExtension(path));
    }

    [Fact]
    public void Save_avoids_collisions_within_the_same_second()
    {
        var first = TextDropService.Save("a", _root, When);
        var second = TextDropService.Save("b", _root, When);
        Assert.NotEqual(first, second);
        Assert.EndsWith("(2).txt", Path.GetFileName(second));
        Assert.Equal("a", File.ReadAllText(first));
        Assert.Equal("b", File.ReadAllText(second));
    }

    [Fact]
    public void Save_avoids_collisions_with_existing_directory()
    {
        var occupied = Path.Combine(_root, "text_2026-07-06_23-15-04.txt");
        Directory.CreateDirectory(occupied);

        var path = TextDropService.Save("directory collision", _root, When);

        Assert.Equal("text_2026-07-06_23-15-04 (2).txt", Path.GetFileName(path));
        Assert.True(Directory.Exists(occupied));
        Assert.Equal("directory collision", File.ReadAllText(path));
    }
}
