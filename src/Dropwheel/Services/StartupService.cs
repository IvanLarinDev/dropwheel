using Microsoft.Win32;

namespace Dropwheel.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Dropwheel";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(AppName) != null;
        }
    }

    public static void SetEnabled(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(AppName, Command());
        else k.DeleteValue(AppName, false);
    }

    /// <summary>The exe can live anywhere and be moved by the user; if autostart
    /// is enabled, point the Run entry at the current location.</summary>
    public static void RefreshPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k?.GetValue(AppName) is string current && current != Command())
                k.SetValue(AppName, Command());
        }
        catch { /* registry unavailable — not critical at startup */ }
    }

    private static string Command() => $"\"{Environment.ProcessPath}\"";
}
