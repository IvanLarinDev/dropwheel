using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies FileMeta reads size/age from disk and degrades safely for missing files.</summary>
public sealed class FileMetaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_meta_" + Guid.NewGuid().ToString("N"));

    public FileMetaTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void Reads_name_extension_and_size()
    {
        var path = Path.Combine(_root, "photo.JPG");
        File.WriteAllBytes(path, new byte[2 * 1024 * 1024]);
        var meta = FileMeta.Read(path);
        Assert.Equal("photo.JPG", meta.Name);
        Assert.Equal("jpg", meta.Ext);
        Assert.True(meta.SizeMb is > 1.9 and < 2.1);
        Assert.True(meta.AgeDays < 1);
    }

    [Fact]
    public void Missing_file_keeps_name_and_extension_with_zero_size()
    {
        var meta = FileMeta.Read(Path.Combine(_root, "ghost.pdf"));
        Assert.Equal("ghost.pdf", meta.Name);
        Assert.Equal("pdf", meta.Ext);
        Assert.Equal(0, meta.SizeMb);
        Assert.Equal(0, meta.AgeDays);
    }
}
