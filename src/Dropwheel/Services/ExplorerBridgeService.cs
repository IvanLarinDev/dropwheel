using System.IO;

namespace Dropwheel.Services;

public static class ExplorerBridgeService
{
    private const string ShortcutName = "Dropwheel.lnk";
    private const string LegacyLauncherName = "Dropwheel.cmd";

    public static string SendToFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "SendTo");

    public static string SendToLauncherPath => ShortcutPath(SendToFolder);

    internal static string LegacySendToLauncherPath => LegacyShortcutPath(SendToFolder);

    public static bool IsSendToInstalled() => IsSendToInstalled(SendToFolder);

    internal static bool IsSendToInstalled(string sendToFolder) =>
        File.Exists(ShortcutPath(sendToFolder)) || File.Exists(LegacyShortcutPath(sendToFolder));

    public static bool NeedsSendToUpgrade() => NeedsSendToUpgrade(SendToFolder);

    internal static bool NeedsSendToUpgrade(string sendToFolder) =>
        File.Exists(LegacyShortcutPath(sendToFolder));

    public static void InstallSendTo(string appPath) => InstallSendTo(appPath, SendToFolder);

    internal static void InstallSendTo(string appPath, string sendToFolder)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("Application path is required.", nameof(appPath));
        if (string.IsNullOrWhiteSpace(sendToFolder))
            throw new ArgumentException("SendTo folder is required.", nameof(sendToFolder));

        Directory.CreateDirectory(sendToFolder);
        CreateShortcut(ShortcutPath(sendToFolder), ShortcutSpec.For(appPath));
        DeleteIfExists(LegacyShortcutPath(sendToFolder));
    }

    public static void UninstallSendTo() => UninstallSendTo(SendToFolder);

    internal static void UninstallSendTo(string sendToFolder)
    {
        if (string.IsNullOrWhiteSpace(sendToFolder))
            throw new ArgumentException("SendTo folder is required.", nameof(sendToFolder));

        DeleteIfExists(ShortcutPath(sendToFolder));
        DeleteIfExists(LegacyShortcutPath(sendToFolder));
    }

    internal static ShortcutSpec BuildShortcutSpec(string appPath) => ShortcutSpec.For(appPath);

    private static void CreateShortcut(string shortcutPath, ShortcutSpec spec)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create a Windows shortcut writer.");

        dynamic shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
        shortcut.TargetPath = spec.TargetPath;
        shortcut.Arguments = spec.Arguments;
        shortcut.WorkingDirectory = spec.WorkingDirectory;
        shortcut.IconLocation = spec.IconLocation;
        shortcut.Description = "Send selected Explorer files to Dropwheel";
        shortcut.Save();
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static string ShortcutPath(string sendToFolder) => Path.Combine(sendToFolder, ShortcutName);

    private static string LegacyShortcutPath(string sendToFolder) => Path.Combine(sendToFolder, LegacyLauncherName);

    internal sealed record ShortcutSpec(
        string TargetPath,
        string Arguments,
        string WorkingDirectory,
        string IconLocation)
    {
        public static ShortcutSpec For(string appPath)
        {
            var workingDirectory = Path.GetDirectoryName(appPath) ?? "";
            if (Path.GetExtension(appPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                return new ShortcutSpec(
                    "dotnet",
                    $"{Quote(appPath)} --sendto",
                    workingDirectory,
                    appPath);

            return new ShortcutSpec(
                appPath,
                "--sendto",
                workingDirectory,
                appPath);
        }

        private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
