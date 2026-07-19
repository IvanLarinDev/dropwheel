using System.IO;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class ExplorerBridgeCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dw_explorer_" + Guid.NewGuid().ToString("N"));

    public ExplorerBridgeCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void Parse_sendto_keeps_existing_paths_and_deduplicates()
    {
        var file = Path.Combine(_root, "note.txt");
        File.WriteAllText(file, "hello");
        var missing = Path.Combine(_root, "missing.txt");

        var command = ExplorerBridgeCommand.Parse(new[] { "--sendto", file, file.ToUpperInvariant(), missing });

        Assert.Equal(ExplorerBridgeCommandKind.SendToFiles, command.Kind);
        var path = Assert.Single(command.Paths);
        Assert.Equal(file, path);
    }

    [Fact]
    public void Parse_install_sendto_accepts_optional_app_path()
    {
        var command = ExplorerBridgeCommand.Parse(new[] { "--install-sendto", @"C:\Tools\Dropwheel.exe" });

        Assert.Equal(ExplorerBridgeCommandKind.InstallSendTo, command.Kind);
        Assert.Equal(@"C:\Tools\Dropwheel.exe", command.AppPath);
    }

    [Fact]
    public void Parse_uninstall_sendto()
    {
        var command = ExplorerBridgeCommand.Parse(new[] { "/uninstall-sendto" });

        Assert.Equal(ExplorerBridgeCommandKind.UninstallSendTo, command.Kind);
    }

    [Fact]
    public void Parse_smoke_test_requires_explicit_profile_and_probe_paths()
    {
        var profile = Path.Combine(_root, "profile");
        var probe = Path.Combine(_root, "probe.txt");
        Directory.CreateDirectory(profile);
        File.WriteAllText(probe, "nonce");

        var command = ExplorerBridgeCommand.Parse(new[] { "--smoke-test", profile, probe });

        Assert.Equal(ExplorerBridgeCommandKind.SmokeTest, command.Kind);
        Assert.Equal(profile, command.SmokeProfileRoot);
        Assert.Equal(probe, command.SmokeProbePath);
        Assert.Empty(command.Paths);
    }

    [Fact]
    public void Parse_smoke_sender_requires_explicit_profile_and_probe_paths()
    {
        var profile = Path.Combine(_root, "sender-profile");
        var probe = Path.Combine(_root, "sender-probe.txt");
        Directory.CreateDirectory(profile);
        File.WriteAllText(probe, "nonce");

        var command = ExplorerBridgeCommand.Parse(new[] { "--smoke-send", profile, probe });

        Assert.Equal(ExplorerBridgeCommandKind.SmokeSendFiles, command.Kind);
        Assert.Equal(profile, command.SmokeProfileRoot);
        Assert.Equal(probe, command.SmokeProbePath);
        Assert.Equal(new[] { probe }, command.Paths);
    }

    [Theory]
    [InlineData("--smoke-test")]
    [InlineData("--smoke-test", "relative-profile")]
    [InlineData("--smoke-send")]
    [InlineData("--smoke-send", "relative-profile")]
    public void Parse_rejects_unisolated_smoke_commands(params string[] args)
    {
        var command = ExplorerBridgeCommand.Parse(args);

        Assert.Equal(ExplorerBridgeCommandKind.Invalid, command.Kind);
    }

    [Fact]
    public void Parse_ignores_plain_startup_args()
    {
        var command = ExplorerBridgeCommand.Parse(new[] { "plain.txt" });

        Assert.Equal(ExplorerBridgeCommandKind.None, command.Kind);
        Assert.Empty(command.Paths);
    }
}
