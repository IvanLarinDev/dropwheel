using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Проверяет, что файловые операции на пустом списке не зовут SHFileOperation
/// со странным пустым pFrom, а сразу отвечают успехом (делать нечего).</summary>
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
