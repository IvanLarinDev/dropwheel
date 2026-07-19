using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace Dropwheel.Tests;

public sealed class ReleaseScriptRegressionTests(ITestOutputHelper output) : IDisposable
{
    private const string RussianSubject = "chore(git): наведение порядка в структуре репозитория";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dw release script " + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Successful_release_verification_returns_only_the_release_object()
    {
        Directory.CreateDirectory(_root);
        var releaseScript = CreateFunctionOnlyReleaseScript();
        File.WriteAllText(
            Path.Combine(_root, "verify-release-assets.ps1"),
            "param([string] $Directory, [string] $Tag, [string] $ExpectedCommit)\n" +
            "Write-Output \"RELEASE_ASSETS_OK diagnostic\"\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var release = new
        {
            tagName = "v1.2.3",
            isDraft = false,
            isPrerelease = false,
            url = "https://example.invalid/releases/v1.2.3",
            targetCommitish = "main",
            assets = Enumerable.Range(1, 5).Select(index => new
            {
                name = index switch
                {
                    1 => "Dropwheel-v1.2.3-win-x64.zip",
                    2 => "Dropwheel-v1.2.3-win-x64-self-contained.zip",
                    3 => "Dropwheel-v1.2.3-PROVENANCE.json",
                    4 => "Dropwheel-v1.2.3-SBOM.spdx.json",
                    _ => "Dropwheel-v1.2.3-SHA256SUMS.txt",
                },
                size = 1,
            }),
        };
        var harness = WriteHarness(
            "release-object-contract.ps1",
            """
            . $env:DW_RELEASE_SCRIPT

            function Invoke-Native {
                param(
                    [string] $FilePath,
                    [string[]] $ArgumentList,
                    [switch] $Capture
                )

                if ($ArgumentList[0] -eq 'release' -and $ArgumentList[1] -eq 'view') {
                    return $env:DW_RELEASE_JSON
                }
                if ($ArgumentList[0] -eq 'release' -and $ArgumentList[1] -eq 'download') {
                    return
                }
                throw "Unexpected native command: $FilePath $($ArgumentList -join ' ')"
            }

            $result = @(Assert-GitHubRelease -Tag 'v1.2.3' -ExpectedCommit '0123456789abcdef0123456789abcdef01234567')
            if ($result.Count -ne 1) {
                $types = @($result | ForEach-Object { $_.GetType().FullName }) -join ', '
                throw "Expected exactly one success-pipeline object; found $($result.Count): $types"
            }
            if ($result[0].url -cne 'https://example.invalid/releases/v1.2.3') {
                throw "The sole pipeline object is not the release object."
            }
            """);
        var environment = new Dictionary<string, string>
        {
            ["DW_RELEASE_SCRIPT"] = releaseScript,
            ["DW_RELEASE_JSON"] = JsonSerializer.Serialize(release),
        };

        foreach (var host in PowerShellHosts())
        {
            var result = RunPowerShell(host, harness, environment);
            Assert.True(result.ExitCode == 0, $"{host}:{Environment.NewLine}{result.Diagnostics}");
            output.WriteLine("Verified exact release-object pipeline under {0}.", host);
        }
    }

    [Fact]
    public void Changelog_preserves_utf8_git_subject_in_supported_powershell_hosts()
    {
        Directory.CreateDirectory(_root);
        var releaseScript = CreateFunctionOnlyReleaseScript();
        var repository = Path.Combine(_root, "repository");
        Directory.CreateDirectory(repository);
        RunGit(repository, "init", "--initial-branch=main");
        RunGit(repository, "config", "user.name", "Release Test");
        RunGit(repository, "config", "user.email", "release-test@example.invalid");
        File.WriteAllText(Path.Combine(repository, "fixture.txt"), "initial");
        RunGit(repository, "add", "fixture.txt");
        RunGit(repository, "commit", "-m", "chore: initial fixture");
        RunGit(repository, "tag", "v0.0.0");
        File.AppendAllText(Path.Combine(repository, "fixture.txt"), "\nрусский текст", Encoding.UTF8);
        RunGit(repository, "add", "fixture.txt");
        RunGit(repository, "commit", "-m", RussianSubject);
        var baseSha = RunGit(repository, "rev-parse", "HEAD").Stdout.Trim();

        foreach (var host in PowerShellHosts())
        {
            var resultPath = Path.Combine(_root, $"changelog-{Path.GetFileNameWithoutExtension(host)}.md");
            var harness = WriteHarness(
                $"utf8-{Path.GetFileNameWithoutExtension(host)}.ps1",
                """
                . $env:DW_RELEASE_SCRIPT
                Set-Location -LiteralPath $env:DW_REPOSITORY
                $section = New-ChangelogSection `
                    -PreviousTag 'v0.0.0' `
                    -TargetTag 'v0.0.1' `
                    -BaseSha $env:DW_BASE_SHA `
                    -Repository 'example/dropwheel' `
                    -NewLine "`n"
                [IO.File]::WriteAllText(
                    $env:DW_RESULT_PATH,
                    $section,
                    [Text.UTF8Encoding]::new($false))
                """);
            var result = RunPowerShell(
                host,
                harness,
                new Dictionary<string, string>
                {
                    ["DW_RELEASE_SCRIPT"] = releaseScript,
                    ["DW_REPOSITORY"] = repository,
                    ["DW_BASE_SHA"] = baseSha,
                    ["DW_RESULT_PATH"] = resultPath,
                });

            Assert.True(result.ExitCode == 0, $"{host}:{Environment.NewLine}{result.Diagnostics}");
            var changelog = File.ReadAllText(resultPath, Encoding.UTF8);
            Assert.Contains("наведение порядка в структуре репозитория", changelog, StringComparison.Ordinal);
            Assert.DoesNotContain("╨", changelog, StringComparison.Ordinal);
            output.WriteLine("Verified UTF-8 changelog generation under {0}.", host);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
        }
        TempDir.Delete(_root);
    }

    private string CreateFunctionOnlyReleaseScript()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "release.ps1"));
        const string executionMarker = "foreach ($tool in @('git', 'dotnet', 'gh')) {";
        Assert.Contains(executionMarker, source, StringComparison.Ordinal);
        var functionOnly = source.Replace(
            executionMarker,
            "return" + Environment.NewLine + executionMarker,
            StringComparison.Ordinal);
        var path = Path.Combine(_root, "release-under-test.ps1");
        File.WriteAllText(path, functionOnly, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private string WriteHarness(string name, string contents)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static IEnumerable<string> PowerShellHosts()
    {
        yield return "powershell.exe";
        var pwshAvailable = ExecutableExistsOnPath("pwsh.exe");
        if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.True(pwshAvailable, "GitHub CI must exercise release regressions under pwsh as well as Windows PowerShell 5.1.");
        if (pwshAvailable)
            yield return "pwsh.exe";
    }

    private static bool ExecutableExistsOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().Trim('"'))
            .Any(entry => File.Exists(Path.Combine(entry, fileName)));
    }

    private static ProcessResult RunPowerShell(
        string host,
        string harness,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", harness })
            startInfo.ArgumentList.Add(argument);
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        return RunProcess(startInfo);
    }

    private static ProcessResult RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        var result = RunProcess(startInfo);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)}:{Environment.NewLine}{result.Diagnostics}");
        return result;
    }

    private static ProcessResult RunProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5_000), $"Timed-out {startInfo.FileName} did not stop after kill.");
            Assert.Fail($"{startInfo.FileName} timed out.");
        }
        Assert.True(
            Task.WaitAll([stdout, stderr], 5_000),
            $"{startInfo.FileName} output did not drain after process exit.");
        return new ProcessResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dropwheel.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Dropwheel repository root.");
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Diagnostics =>
            $"Exit code: {ExitCode}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{Stderr}";
    }
}
