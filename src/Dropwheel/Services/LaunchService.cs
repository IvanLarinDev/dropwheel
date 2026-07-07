using System.Diagnostics;
using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class LaunchService
{
    private static readonly Dictionary<string, string> LnkCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The path a target really points at: a .lnk is resolved to its target (cached to
    /// avoid repeated COM calls during drag-over), anything else is returned unchanged.</summary>
    private static string EffectivePath(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        if (LnkCache.TryGetValue(path, out var cached)) return cached;
        var resolved = ShortcutResolver.Resolve(path);
        LnkCache[path] = resolved;
        return resolved;
    }

    /// <summary>Whether dropping files on this target should run it with them as arguments —
    /// true for executable/script targets, including a .lnk that points at one.</summary>
    public static bool IsRunTarget(TargetItem t) =>
        !t.IsGroup && TargetItem.IsExeExtension(EffectivePath(t.Path));

    public static void Launch(TargetItem t)
    {
        try
        {
            if (t.IsFolder)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{t.Path}\""));
            else
                Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true });
        }
        catch { /* target may no longer exist — ignore */ }
    }

    /// <summary>Quotes each dropped path and joins them for use as command-line arguments.</summary>
    public static string BuildArgs(IEnumerable<string> files) =>
        string.Join(" ", files.Select(f => $"\"{f}\""));

    /// <summary>Runs an executable or script target with the dropped files as arguments — the
    /// Windows "open with" behaviour. Scripts that the shell would open in an editor (.ps1/.py/.jar)
    /// are launched through their interpreter so they actually run. Returns whether it started.</summary>
    public static bool LaunchWith(TargetItem t, IReadOnlyList<string> files)
    {
        var exe = EffectivePath(t.Path);
        var args = BuildArgs(files);
        var psi = Path.GetExtension(exe).ToLowerInvariant() switch
        {
            ".ps1" => new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{exe}\" {args}"),
            ".py" or ".pyw" => new ProcessStartInfo("py", $"\"{exe}\" {args}"),
            ".jar" => new ProcessStartInfo("java", $"-jar \"{exe}\" {args}"),
            _ => new ProcessStartInfo(exe) { Arguments = args, UseShellExecute = true },
        };
        psi.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
        try { Process.Start(psi); return true; }
        catch { return false; }
    }

    public static void OpenConfigFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TargetStore.Dir}\""));
}
