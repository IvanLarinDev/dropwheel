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

    [Theory]
    [InlineData("tg://resolve?domain=telegram")]
    [InlineData("https://t.me/telegram")]
    [InlineData("https://example.com/docs")]
    public void Uri_targets_count_as_existing_quick_access_targets(string path)
    {
        var t = new TargetItem { Name = "link", Path = path };

        Assert.True(t.IsUri);
        Assert.True(t.Exists);
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

    [Fact]
    public void Default_launch_uses_script_interpreters()
    {
        var ps = LaunchService.BuildStartInfo(@"C:\scripts\tool.ps1", new[] { @"C:\drop\a.txt" }, null);
        var py = LaunchService.BuildStartInfo(@"C:\scripts\tool.py", new[] { @"C:\drop\a.txt" }, null);
        var jar = LaunchService.BuildStartInfo(@"C:\scripts\tool.jar", new[] { @"C:\drop\a.txt" }, null);

        Assert.Equal("powershell.exe", ps.FileName);
        Assert.Equal("py", py.FileName);
        Assert.Equal("java", jar.FileName);
    }

    [Fact]
    public void Powershell_launch_passes_each_flag_and_the_script_as_separate_tokens()
    {
        var psi = LaunchService.BuildStartInfo(@"C:\scripts\tool.ps1", new[] { @"C:\drop\a b.txt" }, null);

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.Equal(
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\scripts\tool.ps1", @"C:\drop\a b.txt" },
            psi.ArgumentList);
        Assert.Equal("", psi.Arguments); // nothing hand-built that Windows would re-tokenize
        Assert.False(psi.UseShellExecute); // ArgumentList is honored on the interpreter path
    }

    [Fact]
    public void Interpreter_launch_keeps_a_quote_laden_target_as_one_argument_token()
    {
        // A crafted .lnk can resolve to a target string containing a double quote. Built as a single
        // interpolated Arguments string, that quote would break out and inject extra command-line tokens
        // (e.g. turning `py "{exe}"` into `py -c "<code>"`). ArgumentList must keep it one opaque token.
        var evil = "-c\" \"import os;os.system('calc');#.py";

        var psi = LaunchService.BuildStartInfo(evil, new[] { @"C:\drop\a.txt" }, null);

        Assert.Equal("py", psi.FileName);
        Assert.Equal(new[] { evil, @"C:\drop\a.txt" }, psi.ArgumentList);
        Assert.Equal("", psi.Arguments);
    }

    [Fact]
    public void BuildStartInfo_uses_custom_launch_options_for_one_target()
    {
        var psi = LaunchService.BuildStartInfo(
            @"C:\scripts\tool.bat",
            new[] { @"C:\drop\a.txt", @"C:\drop\b b.txt" },
            new LaunchOptions
            {
                FileName = "runner.exe",
                Arguments = "--script \"{target}\" --dir \"{targetDir}\" -- {files}",
                WorkingDirectory = "{targetDir}",
            });

        Assert.Equal("runner.exe", psi.FileName);
        Assert.Equal("--script \"C:\\scripts\\tool.bat\" --dir \"C:\\scripts\" -- \"C:\\drop\\a.txt\" \"C:\\drop\\b b.txt\"", psi.Arguments);
        Assert.Equal(@"C:\scripts", psi.WorkingDirectory);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void BuildStartInfo_falls_back_to_shell_execution_without_custom_options()
    {
        var psi = LaunchService.BuildStartInfo(@"C:\tools\app.exe", new[] { @"C:\drop\a.txt" }, null);
        Assert.Equal(@"C:\tools\app.exe", psi.FileName);
        Assert.Equal("\"C:\\drop\\a.txt\"", psi.Arguments);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void SortRule_clone_is_deep()
    {
        var original = new SortRule
        {
            Dest = "Images",
            All = { new RuleCondition { Field = ConditionField.Extension, Op = CompareOp.In, Value = "jpg" } },
        };

        var clone = original.Clone();
        clone.Dest = "Edited";
        clone.All[0].Value = "png";

        Assert.Equal("Images", original.Dest);
        Assert.Equal("jpg", original.All[0].Value);
    }
}
