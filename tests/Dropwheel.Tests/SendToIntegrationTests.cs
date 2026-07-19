using System.IO;
using System.Runtime.InteropServices;
using Dropwheel.Services;

namespace Dropwheel.Tests;

[Collection("WindowsIntegration")]
public sealed class SendToIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dw_sendto_" + Guid.NewGuid().ToString("N"));

    public SendToIntegrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TempDir.Delete(_root);

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Install_and_uninstall_round_trip_stays_inside_the_isolated_SendTo_folder()
    {
        var sendToFolder = Directory.CreateDirectory(Path.Combine(_root, "isolated SendTo")).FullName;
        var appFolder = Directory.CreateDirectory(Path.Combine(_root, "published app")).FullName;
        var appPath = Path.Combine(appFolder, "Dropwheel.exe");
        var shortcutPath = Path.Combine(sendToFolder, "Dropwheel.lnk");
        var legacyPath = Path.Combine(sendToFolder, "Dropwheel.cmd");
        var unrelatedPath = Path.Combine(sendToFolder, "keep.txt");
        File.WriteAllBytes(appPath, [0x4d, 0x5a]);
        File.WriteAllText(legacyPath, "legacy");
        File.WriteAllText(unrelatedPath, "keep");

        ExplorerBridgeService.InstallSendTo(appPath, sendToFolder);

        Assert.True(ExplorerBridgeService.IsSendToInstalled(sendToFolder));
        Assert.False(ExplorerBridgeService.NeedsSendToUpgrade(sendToFolder));
        Assert.True(File.Exists(shortcutPath));
        Assert.False(File.Exists(legacyPath));
        Assert.Equal("keep", File.ReadAllText(unrelatedPath));

        var shortcut = ReadShortcut(shortcutPath);
        Assert.Equal(appPath, shortcut.TargetPath, ignoreCase: true);
        Assert.Equal("--sendto", shortcut.Arguments);
        Assert.Equal(appFolder, shortcut.WorkingDirectory, ignoreCase: true);

        ExplorerBridgeService.UninstallSendTo(sendToFolder);

        Assert.False(ExplorerBridgeService.IsSendToInstalled(sendToFolder));
        Assert.False(File.Exists(shortcutPath));
        Assert.False(File.Exists(legacyPath));
        Assert.Equal("keep", File.ReadAllText(unrelatedPath));
    }

    private static ShortcutDetails ReadShortcut(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create a Windows shortcut reader.");
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            return new ShortcutDetails(
                (string)((dynamic)shortcut).TargetPath,
                (string)((dynamic)shortcut).Arguments,
                (string)((dynamic)shortcut).WorkingDirectory);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private sealed record ShortcutDetails(string TargetPath, string Arguments, string WorkingDirectory);
}
