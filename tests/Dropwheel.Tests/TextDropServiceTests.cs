using System.IO;
using Dropwheel.Services;

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
