using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies that file operations on an empty list don't call SHFileOperation with a weird
/// empty pFrom, but return success right away (nothing to do).</summary>
public sealed class FileOpsTests
{
    [Theory]
    [InlineData(DropAction.Copy)]
    [InlineData(DropAction.Move)]
    public void Execute_with_no_files_is_a_noop_success(DropAction action) =>
        Assert.True(FileOps.Execute(Array.Empty<string>(), Path.GetTempPath(), action));

    [Fact]
    public void Delete_with_no_paths_is_a_noop_success() =>
        Assert.True(FileOps.Delete(Array.Empty<string>()));

    [Fact]
    public void Destination_collision_detects_existing_file_with_same_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_collision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(Path.GetTempPath(), "report.txt");
            File.WriteAllText(Path.Combine(root, "report.txt"), "existing");

            Assert.True(FileOps.HasDestinationCollision(new[] { source }, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Destination_collision_ignores_non_colliding_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw_collision_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(FileOps.HasDestinationCollision(new[] { @"C:\drop\new.txt" }, root));
        }
        finally { Directory.Delete(root, true); }
    }
}
