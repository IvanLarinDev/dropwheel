using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class CursorTargetLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_locator_" + Guid.NewGuid().ToString("N"));

    public CursorTargetLocatorTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }

    [Fact]
    public void An_exact_file_name_resolves_to_that_file()
    {
        var file = Path.Combine(_root, "notes.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(file, CursorTargetLocator.ResolveInFolder(_root, "notes.txt"));
    }

    [Fact]
    public void A_subfolder_name_resolves_to_that_folder()
    {
        var sub = Path.Combine(_root, "Projects");
        Directory.CreateDirectory(sub);

        Assert.Equal(sub, CursorTargetLocator.ResolveInFolder(_root, "Projects"));
    }

    [Fact]
    public void A_name_without_extension_matches_the_file_on_disk()
    {
        var file = Path.Combine(_root, "photo.jpg");
        File.WriteAllText(file, "x");

        Assert.Equal(file, CursorTargetLocator.ResolveInFolder(_root, "photo"));
    }

    [Fact]
    public void An_empty_name_resolves_to_the_folder_itself()
    {
        Assert.Equal(_root, CursorTargetLocator.ResolveInFolder(_root, null));
        Assert.Equal(_root, CursorTargetLocator.ResolveInFolder(_root, "   "));
    }

    [Fact]
    public void A_name_that_matches_nothing_resolves_to_null()
    {
        Assert.Null(CursorTargetLocator.ResolveInFolder(_root, "does-not-exist"));
    }

    [Fact]
    public void A_missing_folder_resolves_to_null()
    {
        var missing = Path.Combine(_root, "gone");
        Assert.Null(CursorTargetLocator.ResolveInFolder(missing, "anything"));
        Assert.Null(CursorTargetLocator.ResolveInFolder(missing, null));
    }

    [Fact]
    public void A_blank_folder_resolves_to_null()
    {
        Assert.Null(CursorTargetLocator.ResolveInFolder("", "notes.txt"));
    }
}
