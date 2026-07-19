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

        Assert.Equal(0, RunVerifier());

        File.AppendAllText(Path.Combine(_root, $"Dropwheel-{Tag}-win-x64.zip"), "tampered");

        Assert.NotEqual(0, RunVerifier());
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

        Assert.NotEqual(0, RunVerifier());
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

    private int RunVerifier()
    {
        var script = Path.Combine(RepositoryRoot(), "scripts", "verify-release-assets.ps1");
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Directory", _root, "-Tag", Tag, "-ExpectedCommit", Commit,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the release asset verifier.");
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(5_000), "Timed-out release asset verifier did not stop after kill.");
            Assert.Fail("Release asset verification timed out.");
        }
        return process.ExitCode;
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
}
