using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
    public void Run_helper_selects_a_compatible_dotnet_host_before_building()
    {
        var runCommand = File.ReadAllText(Path.Combine(RepositoryRoot(), "run.cmd"));
        var requiredSelection = new[]
        {
            "call :select_dotnet",
            "call :try_dotnet \"%DOTNET_ROOT%\\dotnet.exe\"",
            "call :try_dotnet \"%USERPROFILE%\\.dotnet\\dotnet.exe\"",
            "where.exe dotnet",
            "pushd \"%~dp0\"",
            "popd",
            "\"%~1\" --version",
            "set \"DOTNET_EXE=%~f1\"",
            "set \"DOTNET_ROOT=%%~dpD\"",
            "set \"PATH=%DOTNET_ROOT%;%PATH%\"",
            "call :invoke_dotnet restore",
            "call :invoke_dotnet build",
            "call :invoke_dotnet publish",
            "\"%DOTNET_EXE%\" %*",
        };

        Assert.All(requiredSelection, contract =>
            Assert.Contains(contract, runCommand, StringComparison.Ordinal));
        Assert.DoesNotContain("\ndotnet ", runCommand.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_helper_sdk_probe_resolves_an_absolute_path_host_outside_the_repository()
    {
        var root = RepositoryRoot();
        var dotnetHost = CompatibleDotnetHost();
        var dotnetRoot = Path.GetDirectoryName(dotnetHost)!;
        var workingDirectory = Path.Combine(
            Path.GetTempPath(), "dw_run_sdk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /c \"\"{Path.Combine(root, "run.cmd")}\" sdk\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.Environment["DOTNET_ROOT"] = Path.Combine(workingDirectory, "missing-dotnet");
            startInfo.Environment["USERPROFILE"] = workingDirectory;
            startInfo.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                dotnetRoot,
                Environment.GetFolderPath(Environment.SpecialFolder.System));

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("run.cmd sdk did not finish in time.");
            }
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var output = standardOutput + standardError;

            Assert.Equal(0, process.ExitCode);
            Assert.Contains($"DOTNET_EXE={dotnetHost}", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"DOTNET_ROOT={dotnetRoot}{Path.DirectorySeparatorChar}", output, StringComparison.OrdinalIgnoreCase);
            var versionLine = output.ReplaceLineEndings("\n").Split('\n')
                .Select(static line => line.Trim())
                .SingleOrDefault(static line => Regex.IsMatch(line, @"^\d+\.\d+\.\d+$"));
            Assert.NotNull(versionLine);
            var selectedVersion = Version.Parse(versionLine);
            using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
            var pinnedVersion = Version.Parse(
                globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);
            Assert.Equal(pinnedVersion.Major, selectedVersion.Major);
            Assert.Equal(pinnedVersion.Minor, selectedVersion.Minor);
            Assert.Equal(pinnedVersion.Build / 100, selectedVersion.Build / 100);
            Assert.True(selectedVersion.Build >= pinnedVersion.Build);
        }
        finally
        {
            TempDir.Delete(workingDirectory);
        }
    }

    [Fact]
    public void Run_helper_uses_cmd_compatible_crlf_line_endings()
    {
        var root = RepositoryRoot();
        var attributes = File.ReadAllText(Path.Combine(root, ".gitattributes"));
        var runCommand = File.ReadAllText(Path.Combine(root, "run.cmd"));

        Assert.Contains("*.cmd   text eol=crlf", attributes, StringComparison.Ordinal);
        Assert.Contains("\r\n", runCommand, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?<!\r)\n"), runCommand);
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
    public void Runtime_baseline_verifies_ipc_delivery_and_graceful_shutdown()
    {
        var root = RepositoryRoot();
        var verifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-runtime-baseline.ps1"));

        Assert.Contains("--smoke-test", verifier, StringComparison.Ordinal);
        Assert.Contains("--smoke-send", verifier, StringComparison.Ordinal);
        Assert.Contains("@(\"--smoke-test\", $profile, $probe)", verifier, StringComparison.Ordinal);
        Assert.Contains("@(\"--smoke-send\", $profile, $probe)", verifier, StringComparison.Ordinal);
        Assert.Contains("Join-Path $profile \"smoke-ack\"", verifier, StringComparison.Ordinal);
        Assert.Contains("Join-Path $profile \"config.json\"", verifier, StringComparison.Ordinal);
        Assert.Contains("Join-Path $profile \"error.log\"", verifier, StringComparison.Ordinal);
        Assert.Contains("[string]::IsNullOrWhiteSpace($errors)", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("$errors -match", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:APPDATA", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:LOCALAPPDATA", verifier, StringComparison.Ordinal);
        Assert.Contains("dropwheel runtime ", verifier, StringComparison.Ordinal);
        Assert.Contains("Start-NativeProcess", verifier, StringComparison.Ordinal);
        Assert.Contains("Stop-ProcessBounded", verifier, StringComparison.Ordinal);
        Assert.Contains("$smokeFailure", verifier, StringComparison.Ordinal);
        Assert.Contains("AggregateException", verifier, StringComparison.Ordinal);
        Assert.Contains("$sender.WaitForExit(5000)", verifier, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(15000)", verifier, StringComparison.Ordinal);
        Assert.Contains("LIFECYCLE_SMOKE_OK", verifier, StringComparison.Ordinal);
        Assert.Contains("profile=isolated", verifier, StringComparison.Ordinal);
        Assert.Contains("probe=matched", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output \"LIFECYCLE_SMOKE_OK", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output \"RUNTIME_BASELINE_OK", verifier, StringComparison.Ordinal);
        Assert.Contains("$verificationFailure", verifier, StringComparison.Ordinal);
        Assert.Contains("$outputCleanupFailure", verifier, StringComparison.Ordinal);
        var outputCleanup = verifier.LastIndexOf("Remove-Item -Recurse -Force $output", StringComparison.Ordinal);
        var successEmission = verifier.LastIndexOf("Complete-Verification $verificationFailure $outputCleanupFailure $successOutput", StringComparison.Ordinal);
        Assert.Contains("function Complete-Verification", verifier, StringComparison.Ordinal);
        Assert.True(outputCleanup >= 0, "Runtime verifier must remove its temporary output.");
        Assert.True(successEmission > outputCleanup, "Success markers must be emitted only after output cleanup succeeds.");

        var app = File.ReadAllText(Path.Combine(root, "src", "Dropwheel", "App.xaml.cs"));
        var profileOverride = app.IndexOf("TargetStore.DirOverride = command.SmokeProfileRoot", StringComparison.Ordinal);
        var configLoad = app.IndexOf("TargetStore.Load()", StringComparison.Ordinal);
        Assert.True(profileOverride >= 0, "Smoke mode must set an explicit TargetStore root.");
        Assert.True(configLoad > profileOverride, "The smoke profile must be set before config is loaded.");
        Assert.Contains("_smokeProbePath", app, StringComparison.Ordinal);
        Assert.Contains("ExplorerBridgeCommandKind.SmokeSendFiles", app, StringComparison.Ordinal);
        Assert.Contains("Shutdown(ExplorerBridgeIpc.TrySendFiles(command.Paths) ? 0 : 3)", app, StringComparison.Ordinal);
        Assert.Contains("SmokeTestProtocol.IsExpectedProbe", app, StringComparison.Ordinal);
        Assert.DoesNotContain("paths.Contains(_smokeProbePath", app, StringComparison.Ordinal);
        Assert.Contains("SmokeTestProtocol.WriteAcknowledgement", app, StringComparison.Ordinal);
        Assert.Contains("SmokeTestProtocol.WriteDeliveryMarker", app, StringComparison.Ordinal);
        Assert.Contains("if (!_exitAfterExplorerDelivery)", app, StringComparison.Ordinal);
        Assert.Contains("InitTray(maintainSystemIntegrations: !_exitAfterExplorerDelivery)", app, StringComparison.Ordinal);

        var tray = File.ReadAllText(Path.Combine(root, "src", "Dropwheel", "App.Tray.cs"));
        Assert.Contains("InitTray(bool maintainSystemIntegrations)", tray, StringComparison.Ordinal);
        Assert.Contains("if (maintainSystemIntegrations)", tray, StringComparison.Ordinal);
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
        Assert.Contains("call :invoke_dotnet restore \"%~dp0Dropwheel.slnx\" --locked-mode", runCommand, StringComparison.Ordinal);
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
    public void Sbom_generator_is_pinned_as_a_repository_tool()
    {
        var root = RepositoryRoot();
        var toolManifestPath = Path.Combine(root, ".config", "dotnet-tools.json");
        Assert.True(File.Exists(toolManifestPath), "The release SBOM generator must be pinned in a repository tool manifest.");

        using var document = JsonDocument.Parse(File.ReadAllText(toolManifestPath));
        var tool = document.RootElement
            .GetProperty("tools")
            .GetProperty("microsoft.sbom.dotnettool");
        Assert.Equal("4.1.5", tool.GetProperty("version").GetString());
        Assert.Equal(new[] { "sbom-tool" }, tool.GetProperty("commands").EnumerateArray().Select(static command => command.GetString()));
        Assert.False(tool.GetProperty("rollForward").GetBoolean());
    }

    [Fact]
    public void Release_requires_provenance_and_sbom_assets()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        var releaseScript = File.ReadAllText(Path.Combine(root, "scripts", "release.ps1"));

        foreach (var assetPattern in new[]
                 {
                     "Dropwheel-$env:RELEASE_TAG-PROVENANCE.json",
                     "Dropwheel-$env:RELEASE_TAG-SBOM.spdx.json",
                 })
        {
            Assert.Contains(assetPattern, workflow, StringComparison.Ordinal);
        }

        Assert.Contains("dotnet sbom-tool generate", workflow, StringComparison.Ordinal);
        Assert.Contains("New-Item -ItemType Directory -Path \"$sbomInput/fd\", \"$sbomInput/sc\", $sbomOutput", workflow, StringComparison.Ordinal);
        var outputDirectory = workflow.IndexOf("New-Item -ItemType Directory -Path \"$sbomInput/fd\", \"$sbomInput/sc\", $sbomOutput", StringComparison.Ordinal);
        var generation = workflow.IndexOf("dotnet sbom-tool generate", StringComparison.Ordinal);
        Assert.True(outputDirectory < generation, "The SBOM manifest output directory must exist before generation.");
        Assert.Contains("dotnet sbom-tool validate", workflow, StringComparison.Ordinal);
        Assert.Contains("-o (Join-Path $sbomOutput 'validation.json')", workflow, StringComparison.Ordinal);
        Assert.Contains("-mi SPDX:2.2", workflow, StringComparison.Ordinal);
        Assert.Contains("-n true", workflow, StringComparison.Ordinal);
        var validation = workflow.IndexOf("dotnet sbom-tool validate", StringComparison.Ordinal);
        var releaseCopy = workflow.IndexOf("Copy-Item -LiteralPath $generatedSbom -Destination $sbom", StringComparison.Ordinal);
        Assert.True(generation < validation && validation < releaseCopy, "SBOM validation must succeed before the release asset is copied.");
        Assert.Contains("-bc (Join-Path $PWD 'src/Dropwheel')", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-bc $PWD", workflow, StringComparison.Ordinal);
        Assert.Contains("sdkVersion = (dotnet --version).Trim()", workflow, StringComparison.Ordinal);
        Assert.Contains("runtimeIdentifier = 'win-x64'", workflow, StringComparison.Ordinal);
        Assert.Contains("runtimePacks = $runtimePacks", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$_.version.Trim('[', ']')", workflow, StringComparison.Ordinal);
        Assert.Contains("$rangeParts.Count -ne 2", workflow, StringComparison.Ordinal);
        Assert.Contains("[string]::Equals($rangeParts[0], $rangeParts[1], [StringComparison]::Ordinal)", workflow, StringComparison.Ordinal);
        Assert.Contains("src/Dropwheel/packages.lock.json", workflow, StringComparison.Ordinal);
        Assert.Contains("tests/Dropwheel.Tests/packages.lock.json", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash $fd, $sc, $provenance, $sbom", workflow, StringComparison.Ordinal);
        Assert.Contains("$checksumLines.Count -ne 4", workflow, StringComparison.Ordinal);
        Assert.Contains("PROVENANCE.json", releaseScript, StringComparison.Ordinal);
        Assert.Contains("SBOM.spdx.json", releaseScript, StringComparison.Ordinal);
        Assert.Contains("'release', 'download', $Tag", releaseScript, StringComparison.Ordinal);
        Assert.Contains("@($release.assets).Count -ne $requiredAssets.Count", releaseScript, StringComparison.Ordinal);
        Assert.Contains("verify-release-assets.ps1", releaseScript, StringComparison.Ordinal);
        Assert.Contains("-ExpectedCommit $ExpectedCommit", releaseScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_provenance_is_documented_for_operators()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "scripts", "README.md"));

        Assert.Contains("PROVENANCE.json", readme, StringComparison.Ordinal);
        Assert.Contains("SBOM.spdx.json", readme, StringComparison.Ordinal);
        Assert.Contains("five required assets", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify-release-assets.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("recomputes SHA-256", readme, StringComparison.Ordinal);
        Assert.Contains("all four content assets", readme, StringComparison.Ordinal);
        Assert.Contains("commit", readme, StringComparison.Ordinal);
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

        var releaseWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        Assert.Contains("persist-credentials: false", releaseWorkflow, StringComparison.Ordinal);
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

    private static string CompatibleDotnetHost()
    {
        for (var directory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "dotnet.exe");
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException("Could not locate the dotnet host for the running test process.");
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
