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
    public void Parse_ignores_plain_startup_args()
    {
        var command = ExplorerBridgeCommand.Parse(new[] { "plain.txt" });

        Assert.Equal(ExplorerBridgeCommandKind.None, command.Kind);
        Assert.Empty(command.Paths);
    }
}
