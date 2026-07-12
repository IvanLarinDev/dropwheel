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
        var psi = BuildStartInfo(exe, files, t.Launch);
        try { Process.Start(psi); return true; }
        catch (Exception ex) { ErrorLog.Write($"Could not launch '{exe}' with dropped files", ex); return false; }
    }

    public static void OpenConfigFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{TargetStore.Dir}\""));

    internal static ProcessStartInfo BuildStartInfo(string exe, IReadOnlyList<string> files, LaunchOptions? custom)
    {
        var args = BuildArgs(files);
        var targetDir = Path.GetDirectoryName(exe) ?? "";
        var psi = custom == null
            ? DefaultStartInfo(exe, files)
            : new ProcessStartInfo(
                ExpandTemplate(custom.FileName, exe, args, targetDir),
                ExpandTemplate(custom.Arguments, exe, args, targetDir))
            { UseShellExecute = true };
        psi.WorkingDirectory = custom == null
            ? targetDir
            : ExpandTemplate(custom.WorkingDirectory, exe, args, targetDir);
        return psi;
    }

    /// <summary>Builds the launch for an interpreter/executable target. The interpreter branches pass
    /// the script path and every dropped file as SEPARATE ArgumentList entries (UseShellExecute defaults
    /// to false there), so Windows never re-tokenizes them. Hand-building a quoted argument string here
    /// would let a crafted target path — e.g. one resolved from a malicious .lnk — break out of its
    /// quotes and inject extra command-line tokens, i.e. run arbitrary code through the interpreter.</summary>
    private static ProcessStartInfo DefaultStartInfo(string exe, IReadOnlyList<string> files)
    {
        return Path.GetExtension(exe).ToLowerInvariant() switch
        {
            ".ps1" => WithFiles(WithArgs("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", exe), files),
            ".py" or ".pyw" => WithFiles(WithArgs("py", exe), files),
            ".jar" => WithFiles(WithArgs("java", "-jar", exe), files),
            _ => new ProcessStartInfo(exe) { Arguments = BuildArgs(files), UseShellExecute = true },
        };
    }

    /// <summary>A ProcessStartInfo whose argument tokens are supplied individually (correct per-argument
    /// quoting), never as a single hand-interpolated string.</summary>
    private static ProcessStartInfo WithArgs(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static ProcessStartInfo WithFiles(ProcessStartInfo psi, IReadOnlyList<string> files)
    {
        foreach (var f in files) psi.ArgumentList.Add(f);
        return psi;
    }

    internal static string ExpandTemplate(string template, string target, string files, string targetDir) =>
        template.Replace("{target}", target, StringComparison.Ordinal)
                .Replace("{targetDir}", targetDir, StringComparison.Ordinal)
                .Replace("{files}", files, StringComparison.Ordinal)
                .Trim();
}
