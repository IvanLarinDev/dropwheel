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

    public static string SendToLauncherPath => Path.Combine(SendToFolder, ShortcutName);

    internal static string LegacySendToLauncherPath => Path.Combine(SendToFolder, LegacyLauncherName);

    public static bool IsSendToInstalled() =>
        File.Exists(SendToLauncherPath) || File.Exists(LegacySendToLauncherPath);

    public static bool NeedsSendToUpgrade() => File.Exists(LegacySendToLauncherPath);

    public static void InstallSendTo(string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("Application path is required.", nameof(appPath));

        Directory.CreateDirectory(SendToFolder);
        CreateShortcut(SendToLauncherPath, ShortcutSpec.For(appPath));
        DeleteIfExists(LegacySendToLauncherPath);
    }

    public static void UninstallSendTo()
    {
        DeleteIfExists(SendToLauncherPath);
        DeleteIfExists(LegacySendToLauncherPath);
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
