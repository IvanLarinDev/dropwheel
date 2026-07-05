using System.Diagnostics;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class LaunchService
{
    public static void Launch(TargetItem t)
    {
        try
        {
            if (t.IsFolder)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{t.Path}\""));
            else
                Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true });
        }
        catch { /* цель могла исчезнуть — молча игнорируем */ }
    }

    public static void OpenConfigFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TargetStore.Dir}\""));
}
