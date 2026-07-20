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
        var targetDir = Path.GetDirectoryName(exe) ?? "";
        var psi = custom == null
            ? DefaultStartInfo(exe, files)
            : CustomStartInfo(custom, exe, files, targetDir);
        psi.WorkingDirectory = custom == null
            ? targetDir
            : ExpandScalar(custom.WorkingDirectory, exe, targetDir);
        return psi;
    }

    private static ProcessStartInfo CustomStartInfo(
        LaunchOptions custom,
        string target,
        IReadOnlyList<string> files,
        string targetDir)
    {
        var fileName = ExpandScalar(custom.FileName, target, targetDir);
        var tokens = TokenizeArguments(custom.Arguments);
        var expandsFiles = tokens.Any(token => token.Contains("{files}", StringComparison.Ordinal));
        var expandsDynamicValue = expandsFiles || tokens.Any(token =>
            token.Contains("{target}", StringComparison.Ordinal)
            || token.Contains("{targetDir}", StringComparison.Ordinal));
        if (expandsDynamicValue && IsCommandShellMode(fileName, tokens))
            throw new InvalidOperationException(
                "Dynamic placeholders cannot be used with a shell command mode. Use an executable or script-file mode instead.");

        var psi = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var token in tokens)
        {
            if (token == "{files}")
            {
                foreach (var file in files) psi.ArgumentList.Add(file);
                continue;
            }
            if (token.Contains("{files}", StringComparison.Ordinal))
                throw new InvalidOperationException("{files} must be a separate argument token.");
            psi.ArgumentList.Add(ExpandScalar(token, target, targetDir));
        }
        return psi;
    }

    private static bool IsCommandShellMode(string fileName, IReadOnlyList<string> tokens)
    {
        var program = Path.GetFileNameWithoutExtension(fileName);
        if (program.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || program.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            return tokens.Any(IsPowerShellCommandSwitch);
        if (program.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            return tokens.Any(IsCmdCommandSwitch);
        if (program.Equals("python", StringComparison.OrdinalIgnoreCase)
            || program.Equals("py", StringComparison.OrdinalIgnoreCase))
            return tokens.Any(token => token.Equals("--command", StringComparison.OrdinalIgnoreCase)
                || IsClusteredCommandSwitch(token));
        if (program.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || program.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || program.Equals("wsl", StringComparison.OrdinalIgnoreCase))
            return tokens.Any(IsPosixShellCommandSwitch);
        return false;
    }

    private static bool IsCmdCommandSwitch(string token)
    {
        if (token.Length < 2 || token[0] != '/') return false;
        for (var i = 1; i < token.Length; i++)
        {
            if ((i == 1 || token[i - 1] == '/')
                && (token[i] is 'c' or 'C' or 'k' or 'K'))
                return true;
        }
        return false;
    }

    private static bool IsPosixShellCommandSwitch(string token) =>
        token.Equals("--command", StringComparison.OrdinalIgnoreCase)
        || IsClusteredCommandSwitch(token);

    private static bool IsClusteredCommandSwitch(string token) =>
        token.Length > 1
        && token[0] == '-'
        && token[1] != '-'
        && token.AsSpan(1).Contains('c');

    /// <summary>PowerShell accepts unique parameter abbreviations (for example -Com for -Command),
    /// so checking a few exact spellings is unsafe. Match every accepted prefix of a code-bearing
    /// switch, including colon/equals attached forms, while leaving unrelated switches alone.</summary>
    private static bool IsPowerShellCommandSwitch(string token)
    {
        var name = token.TrimStart('-', '/');
        var separator = name.IndexOfAny(':', '=');
        if (separator >= 0) name = name[..separator];
        return name.Length > 0
            && ("command".StartsWith(name, StringComparison.OrdinalIgnoreCase)
                || "commandwithargs".StartsWith(name, StringComparison.OrdinalIgnoreCase)
                || "encodedcommand".StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Splits a launch template into Windows-style argument tokens while removing grouping
    /// quotes. Placeholders are expanded only after tokenization, so file paths remain opaque values.</summary>
    internal static IReadOnlyList<string> TokenizeArguments(string arguments)
    {
        var result = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < arguments.Length; i++)
        {
            var ch = arguments[i];
            if (ch == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (token.Length > 0) { result.Add(token.ToString()); token.Clear(); }
                continue;
            }
            token.Append(ch);
        }
        if (quoted) throw new InvalidOperationException("Custom launch arguments contain an unmatched quote.");
        if (token.Length > 0) result.Add(token.ToString());
        return result;
    }

    private static string ExpandScalar(string template, string target, string targetDir)
    {
        if (template.Contains("{files}", StringComparison.Ordinal))
            throw new InvalidOperationException("{files} is only valid in the arguments field.");
        return template.Replace("{target}", target, StringComparison.Ordinal)
            .Replace("{targetDir}", targetDir, StringComparison.Ordinal)
            .Trim();
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
