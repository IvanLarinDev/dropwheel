using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dropwheel.Tests;

public sealed class ReleaseAssetVerificationTests : IDisposable
{
    private const string Tag = "v1.2.3";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dw release assets " + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Verifier_rejects_tampered_content_after_accepting_exact_assets()
    {
        Directory.CreateDirectory(_root);
        WriteValidFixture();

        var accepted = RunVerifier();
        Assert.True(accepted.ExitCode == 0, accepted.Diagnostics);

        File.AppendAllText(Path.Combine(_root, $"Dropwheel-{Tag}-win-x64.zip"), "tampered");

        Assert.NotEqual(0, RunVerifier().ExitCode);
    }

    [Fact]
    public void Verifier_rejects_checksum_valid_provenance_for_another_commit()
    {
        Directory.CreateDirectory(_root);
        WriteValidFixture();
        var provenanceName = $"Dropwheel-{Tag}-PROVENANCE.json";
        File.WriteAllText(
            Path.Combine(_root, provenanceName),
            JsonSerializer.Serialize(new
            {
                source = new
                {
                    tag = Tag,
                    commit = "ffffffffffffffffffffffffffffffffffffffff",
                },
            }));
        RewriteChecksum(provenanceName);

        Assert.NotEqual(0, RunVerifier().ExitCode);
    }

    [Fact]
    public void Verifier_does_not_depend_on_GetFileHash_cmdlet()
    {
        Directory.CreateDirectory(_root);
        WriteValidFixture();

        var result = RunVerifier(simulateMissingGetFileHash: true);

        Assert.True(result.ExitCode == 0, result.Diagnostics);
    }

    public void Dispose() => TempDir.Delete(_root);

    private void WriteValidFixture()
    {
        var contentAssets = new[]
        {
            $"Dropwheel-{Tag}-win-x64.zip",
            $"Dropwheel-{Tag}-win-x64-self-contained.zip",
            $"Dropwheel-{Tag}-PROVENANCE.json",
            $"Dropwheel-{Tag}-SBOM.spdx.json",
        };
        File.WriteAllText(Path.Combine(_root, contentAssets[0]), "framework-dependent");
        File.WriteAllText(Path.Combine(_root, contentAssets[1]), "self-contained");
        File.WriteAllText(
            Path.Combine(_root, contentAssets[2]),
            JsonSerializer.Serialize(new { source = new { tag = Tag, commit = Commit } }));
        File.WriteAllText(
            Path.Combine(_root, contentAssets[3]),
            JsonSerializer.Serialize(new
            {
                spdxVersion = "SPDX-2.2",
                name = "Dropwheel",
                packages = new[] { new { name = "Dropwheel" } },
                files = new[] { new { fileName = "Dropwheel.exe" } },
            }));

        var checksumLines = contentAssets.Select(name =>
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, name))))
                .ToLowerInvariant();
            return $"{hash}  {name}";
        });
        File.WriteAllLines(Path.Combine(_root, $"Dropwheel-{Tag}-SHA256SUMS.txt"), checksumLines);
    }

    private void RewriteChecksum(string assetName)
    {
        var assetPath = Path.Combine(_root, assetName);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assetPath))).ToLowerInvariant();
        var checksumPath = Path.Combine(_root, $"Dropwheel-{Tag}-SHA256SUMS.txt");
        var lines = File.ReadAllLines(checksumPath)
            .Select(line => line.EndsWith("  " + assetName, StringComparison.Ordinal)
                ? $"{hash}  {assetName}"
                : line);
        File.WriteAllLines(checksumPath, lines);
    }

    private VerifierResult RunVerifier(bool simulateMissingGetFileHash = false)
    {
        var script = Path.Combine(RepositoryRoot(), "scripts", "verify-release-assets.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var arguments = simulateMissingGetFileHash
            ? new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                "function Get-FileHash { throw [System.Management.Automation.CommandNotFoundException]::new('Get-FileHash is unavailable.') }; & $env:DW_VERIFIER -Directory $env:DW_DIRECTORY -Tag $env:DW_TAG -ExpectedCommit $env:DW_COMMIT",
            }
            : new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                "-Directory", _root, "-Tag", Tag, "-ExpectedCommit", Commit,
            };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["DW_VERIFIER"] = script;
        startInfo.Environment["DW_DIRECTORY"] = _root;
        startInfo.Environment["DW_TAG"] = Tag;
        startInfo.Environment["DW_COMMIT"] = Commit;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the release asset verifier.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5_000), "Timed-out release asset verifier did not stop after kill.");
            Assert.Fail("Release asset verification timed out.");
        }
        Assert.True(
            Task.WaitAll([stdout, stderr], 5_000),
            "Release asset verifier output did not drain after process exit.");
        return new VerifierResult(process.ExitCode, stdout.Result, stderr.Result);
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

    private readonly record struct VerifierResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Diagnostics =>
            $"Exit code: {ExitCode}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{Stderr}";
    }
}
