using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Covers filename sanitization and collision numbering for saved virtual files.</summary>
public sealed class UniquePathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dw_uniq_" + Guid.NewGuid().ToString("N"));

    public UniquePathTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void Uses_the_name_as_is_when_nothing_collides()
    {
        Assert.Equal(Path.Combine(_dir, "report.pdf"), VirtualFileService.UniquePath(_dir, "report.pdf"));
    }

    [Fact]
    public void Replaces_invalid_filename_characters()
    {
        var result = VirtualFileService.UniquePath(_dir, "a/b:c*d.txt");
        Assert.Equal(Path.Combine(_dir, "a_b_c_d.txt"), result);
    }

    [Fact]
    public void Blank_name_becomes_file()
    {
        Assert.Equal(Path.Combine(_dir, "file"), VirtualFileService.UniquePath(_dir, "   "));
    }

    [Fact]
    public void Numbers_a_collision_keeping_the_extension()
    {
        File.WriteAllText(Path.Combine(_dir, "note.txt"), "x");

        Assert.Equal(Path.Combine(_dir, "note (2).txt"), VirtualFileService.UniquePath(_dir, "note.txt"));
    }

    [Fact]
    public void Skips_to_the_next_free_number_on_repeated_collisions()
    {
        File.WriteAllText(Path.Combine(_dir, "note.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "note (2).txt"), "x");

        Assert.Equal(Path.Combine(_dir, "note (3).txt"), VirtualFileService.UniquePath(_dir, "note.txt"));
    }
}
