using System.Diagnostics;
using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class LaunchService
{
    private static readonly Dictionary<string, string> LnkCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The path a target really points at: a .lnk is resolved to its target (cached to
    /// avoid repeated COM calls during drag-over), anything else is returned unchanged.</summary>
    public static string EffectivePath(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        if (LnkCache.TryGetValue(path, out var cached)) return cached;
        var resolved = ShortcutResolver.Resolve(path);
        LnkCache[path] = resolved;
        return resolved;
    }

    /// <summary>Destination folder for a folder target: the target's real path, with a .lnk
    /// resolved to the folder it points at. Used as the copy/move destination for a drop.</summary>
    public static string DestPath(TargetItem t) => EffectivePath(t.Path);

    /// <summary>Whether files can be dropped into this target as into a folder — true for a real
    /// folder and for a .lnk that points at one (unlike TargetItem.IsFolder, which tests the raw
    /// path and so misses shortcut-to-folder targets).</summary>
    public static bool IsFolderTarget(TargetItem t) =>
        !t.IsGroup && Directory.Exists(EffectivePath(t.Path));

    /// <summary>Whether dropping files on this target should run it with them as arguments —
    /// true for executable/script targets, including a .lnk that points at one.</summary>
    public static bool IsRunTarget(TargetItem t) =>
        !t.IsGroup && IsRunExtension(EffectivePath(t.Path));

    public static void Launch(TargetItem t)
    {
        try
        {
            if (t.IsFolder)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{t.Path}\""));
            else
                Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true });
        }
        catch (Exception ex) { ErrorLog.Write($"Could not launch target '{t.Name}' at '{t.Path}'", ex); }
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
        var psi = BuildStartInfo(exe, files, TargetStore.Config.LaunchCommands);
        psi.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
        try { Process.Start(psi); return true; }
        catch (Exception ex) { ErrorLog.Write($"Could not launch '{exe}' with dropped files", ex); return false; }
    }

    public static void OpenConfigFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TargetStore.Dir}\""));

    internal static ProcessStartInfo BuildStartInfo(
        string exe, IReadOnlyList<string> files, IReadOnlyList<LaunchCommand>? commands)
    {
        var args = BuildArgs(files);
        var command = FindCommand(Path.GetExtension(exe), commands);
        if (command == null)
            return new ProcessStartInfo(exe) { Arguments = args, UseShellExecute = true };

        return new ProcessStartInfo(command.FileName, ExpandArgs(command.Arguments, exe, args));
    }

    private static bool IsRunExtension(string path) =>
        TargetItem.IsExeExtension(path) || FindCommand(Path.GetExtension(path), TargetStore.Config.LaunchCommands) != null;

    private static LaunchCommand? FindCommand(string extension, IReadOnlyList<LaunchCommand>? commands)
    {
        var ext = NormalizeExt(extension);
        if (ext.Length == 0 || commands == null) return null;
        return commands.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.FileName)
            && c.Extensions.Any(e => string.Equals(NormalizeExt(e), ext, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeExt(string extension)
    {
        var ext = extension.Trim();
        if (ext.Length == 0) return "";
        return ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
    }

    private static string ExpandArgs(string template, string target, string files) =>
        template.Replace("{target}", target, StringComparison.Ordinal)
                .Replace("{files}", files, StringComparison.Ordinal)
                .Trim();
}
