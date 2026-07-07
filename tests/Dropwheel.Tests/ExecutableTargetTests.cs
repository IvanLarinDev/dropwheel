using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies executable-target detection and the "open with" argument building.</summary>
public sealed class ExecutableTargetTests
{
    [Theory]
    [InlineData("C:\\tools\\build.exe", true)]
    [InlineData("C:\\tools\\run.BAT", true)]
    [InlineData("C:\\tools\\task.cmd", true)]
    [InlineData("C:\\tools\\legacy.com", true)]
    [InlineData("C:\\tools\\script.ps1", true)]
    [InlineData("C:\\tools\\tool.py", true)]
    [InlineData("C:\\tools\\app.jar", true)]
    [InlineData("C:\\tools\\notes.txt", false)]
    [InlineData("C:\\tools\\image.png", false)]
    [InlineData("C:\\tools\\link.lnk", false)]
    [InlineData("C:\\Downloads", false)]
    public void IsExecutable_matches_only_executables(string path, bool expected)
    {
        var t = new TargetItem { Name = "x", Path = path };
        Assert.Equal(expected, t.IsExecutable);
    }

    [Fact]
    public void Group_is_never_executable()
    {
        var t = new TargetItem { Name = "grp", Path = "C:\\x.exe", Children = new() };
        Assert.False(t.IsExecutable);
    }

    [Fact]
    public void BuildArgs_quotes_and_joins_paths()
    {
        var args = LaunchService.BuildArgs(new[] { @"C:\a b\file 1.txt", @"C:\c\file2.txt" });
        Assert.Equal("\"C:\\a b\\file 1.txt\" \"C:\\c\\file2.txt\"", args);
    }

    [Fact]
    public void BuildArgs_empty_is_empty()
    {
        Assert.Equal("", LaunchService.BuildArgs(Array.Empty<string>()));
    }
}
