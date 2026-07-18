using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dropwheel.Tests;

public sealed class BuildConfigurationTests
{
    [Fact]
    public void Dotnet_sdk_is_pinned_consistently()
    {
        var root = RepositoryRoot();
        var globalJsonPath = Path.Combine(root, "global.json");
        Assert.True(File.Exists(globalJsonPath), "global.json must pin the repository SDK.");

        using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
        var sdk = document.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.302", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        foreach (var workflow in new[] { "ci.yml", "release.yml" })
        {
            var contents = File.ReadAllText(Path.Combine(root, ".github", "workflows", workflow));
            Assert.Contains("global-json-file: global.json", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet-version:", contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runtime_baseline_is_enforced_in_ci_and_documentation()
    {
        var root = RepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "Dropwheel", "Dropwheel.csproj"));
        Assert.Contains("<TargetLatestRuntimePatch>true</TargetLatestRuntimePatch>", project, StringComparison.Ordinal);

        foreach (var workflow in new[] { "ci.yml", "release.yml" })
        {
            var contents = File.ReadAllText(Path.Combine(root, ".github", "workflows", workflow));
            Assert.Contains("./scripts/verify-runtime-baseline.ps1", contents, StringComparison.Ordinal);
        }

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains(".NET 10.0.10 Desktop Runtime", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Nuget_dependencies_are_pinned_locked_and_audited()
    {
        var root = RepositoryRoot();
        var propsPath = Path.Combine(root, "Directory.Build.props");
        Assert.True(File.Exists(propsPath), "Directory.Build.props must enable repository-wide lock policy.");
        var props = File.ReadAllText(propsPath);
        Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", props, StringComparison.Ordinal);
        Assert.Contains("<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>", props, StringComparison.Ordinal);
        Assert.Contains("<NuGetAuditMode>all</NuGetAuditMode>", props, StringComparison.Ordinal);
        Assert.Contains("<WarningsAsErrors>$(WarningsAsErrors);NU1900;NU1901;NU1902;NU1903;NU1904;NU1905</WarningsAsErrors>", props, StringComparison.Ordinal);

        var nugetConfigPath = Path.Combine(root, "NuGet.config");
        Assert.True(File.Exists(nugetConfigPath), "NuGet.config must clear inherited package sources.");
        var nugetConfig = File.ReadAllText(nugetConfigPath);
        Assert.Contains("<clear />", nugetConfig, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", nugetConfig, StringComparison.Ordinal);

        var project = File.ReadAllText(Path.Combine(root, "src", "Dropwheel", "Dropwheel.csproj"));
        Assert.Contains("<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>", project, StringComparison.Ordinal);

        var testProject = File.ReadAllText(Path.Combine(root, "tests", "Dropwheel.Tests", "Dropwheel.Tests.csproj"));
        Assert.Contains("Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.8.1\"", testProject, StringComparison.Ordinal);
        Assert.Contains("Include=\"xunit.runner.visualstudio\" Version=\"3.1.5\"", testProject, StringComparison.Ordinal);
        Assert.DoesNotContain("coverlet.collector", testProject, StringComparison.OrdinalIgnoreCase);

        foreach (var lockFile in new[]
                 {
                     Path.Combine(root, "src", "Dropwheel", "packages.lock.json"),
                     Path.Combine(root, "tests", "Dropwheel.Tests", "packages.lock.json"),
                 })
        {
            Assert.True(File.Exists(lockFile), $"Missing NuGet lock file: {lockFile}");
        }

        var runCommand = File.ReadAllText(Path.Combine(root, "run.cmd"));
        Assert.Contains("dotnet restore \"%~dp0Dropwheel.slnx\" --locked-mode", runCommand, StringComparison.Ordinal);
        Assert.Contains("--no-restore", runCommand, StringComparison.Ordinal);

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("dotnet restore Dropwheel.slnx --locked-mode", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet run --project src/Dropwheel/Dropwheel.csproj --configuration Release --no-restore", readme, StringComparison.Ordinal);

        foreach (var relativeReadme in new[]
                 {
                     Path.Combine("src", "Dropwheel", "README.md"),
                     Path.Combine("tests", "Dropwheel.Tests", "README.md"),
                 })
        {
            var contents = File.ReadAllText(Path.Combine(root, relativeReadme));
            Assert.Contains("dotnet restore Dropwheel.slnx --locked-mode", contents, StringComparison.Ordinal);
            Assert.Contains("--no-restore", contents, StringComparison.Ordinal);
        }

        var releaseScript = File.ReadAllText(Path.Combine(root, "scripts", "release.ps1"));
        Assert.Contains("Invoke-Native dotnet @('restore', 'Dropwheel.slnx', '--nologo', '--locked-mode')", releaseScript, StringComparison.Ordinal);
        Assert.Contains("'test', 'tests/Dropwheel.Tests/Dropwheel.Tests.csproj', '--nologo', '-c', 'Release', '--no-restore'", releaseScript, StringComparison.Ordinal);
        Assert.Equal(3, releaseScript.Split("'--no-restore'", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, releaseScript.Split("'-r', 'win-x64'", StringSplitOptions.None).Length - 1);

        foreach (var workflow in new[] { "ci.yml", "release.yml" })
        {
            var contents = File.ReadAllText(Path.Combine(root, ".github", "workflows", workflow));
            Assert.Contains("dotnet restore Dropwheel.slnx --nologo --locked-mode", contents, StringComparison.Ordinal);
        }

        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        Assert.Equal(2, release.Split("--runtime win-x64", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Dependency_automation_is_scoped_and_runner_family_is_stable()
    {
        var root = RepositoryRoot();
        var dependabotPath = Path.Combine(root, ".github", "dependabot.yml");
        Assert.True(File.Exists(dependabotPath), "Dependabot must maintain locked NuGet and action dependencies.");
        var dependabot = File.ReadAllText(dependabotPath);
        Assert.Contains("package-ecosystem: nuget", dependabot, StringComparison.Ordinal);
        Assert.Contains("package-ecosystem: github-actions", dependabot, StringComparison.Ordinal);
        Assert.Equal(2, dependabot.Split("interval: weekly", StringSplitOptions.None).Length - 1);
        Assert.Contains("groups:", dependabot, StringComparison.Ordinal);

        foreach (var workflow in new[] { "ci.yml", "release.yml" })
        {
            var contents = File.ReadAllText(Path.Combine(root, ".github", "workflows", workflow));
            Assert.Contains("runs-on: windows-2025", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("windows-latest", contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Documented_dotnet_commands_are_restore_closed()
    {
        var root = RepositoryRoot();
        var commandPattern = new Regex(
            @"dotnet\s+(?<verb>restore|build|test|run|publish)\b(?<arguments>[^`]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var readme in Directory.EnumerateFiles(root, "README.md", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, readme);
            if (relativePath.Split(Path.DirectorySeparatorChar)
                .Any(static segment => segment is ".git" or "bin" or "obj" or "artifacts"))
            {
                continue;
            }

            foreach (var line in File.ReadLines(readme))
            {
                foreach (Match match in commandPattern.Matches(line))
                {
                    var requiredSwitch = match.Groups["verb"].Value.Equals("restore", StringComparison.OrdinalIgnoreCase)
                        ? "--locked-mode"
                        : "--no-restore";
                    Assert.Contains(requiredSwitch, match.Groups["arguments"].Value, StringComparison.Ordinal);
                }
            }
        }
    }

    private static string RepositoryRoot()
    {
        var injectedRoot = typeof(BuildConfigurationTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "RepositoryRoot")
            ?.Value;
        var starts = new[]
        {
            injectedRoot,
            Environment.GetEnvironmentVariable("DROPWHEEL_REPOSITORY_ROOT"),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        foreach (var start in starts.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            for (var directory = new DirectoryInfo(start!);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Dropwheel.slnx")))
                    return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Dropwheel repository root.");
    }
}
