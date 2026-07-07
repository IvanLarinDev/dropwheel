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
}
