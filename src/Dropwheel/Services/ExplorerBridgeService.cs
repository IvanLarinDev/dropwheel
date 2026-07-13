using System.IO;

namespace Dropwheel.Services;

public static class ExplorerBridgeService
{
    public static string SendToFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "SendTo");

    public static string SendToLauncherPath => Path.Combine(SendToFolder, "Dropwheel.cmd");

    public static bool IsSendToInstalled() => File.Exists(SendToLauncherPath);

    public static void InstallSendTo(string appPath)
    {
        if (string.IsNullOrWhiteSpace(appPath))
            throw new ArgumentException("Application path is required.", nameof(appPath));

        Directory.CreateDirectory(SendToFolder);
        File.WriteAllText(SendToLauncherPath, LauncherText(appPath));
    }

    public static void UninstallSendTo()
    {
        if (File.Exists(SendToLauncherPath)) File.Delete(SendToLauncherPath);
    }

    internal static string LauncherText(string appPath)
    {
        var escaped = appPath.Replace("\"", "\"\"");
        var command = Path.GetExtension(appPath).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            ? $"dotnet \"{escaped}\""
            : $"\"{escaped}\"";
        return $"@echo off{Environment.NewLine}start \"\" {command} --sendto %*{Environment.NewLine}";
    }
}
