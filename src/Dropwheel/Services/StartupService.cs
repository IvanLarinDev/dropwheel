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
        if (on) k.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(AppName, false);
    }
}
