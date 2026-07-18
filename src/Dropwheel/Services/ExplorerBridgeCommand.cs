using System.IO;

namespace Dropwheel.Services;

public enum ExplorerBridgeCommandKind { None, Invalid, SendToFiles, InstallSendTo, UninstallSendTo, SmokeTest, SmokeSendFiles }

public sealed record ExplorerBridgeCommand(
    ExplorerBridgeCommandKind Kind,
    string[] Paths,
    string? AppPath = null,
    string? SmokeProfileRoot = null,
    string? SmokeProbePath = null)
{
    public static ExplorerBridgeCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new ExplorerBridgeCommand(ExplorerBridgeCommandKind.None, []);

        var first = args[0];
        if (IsSwitch(first, "sendto") || IsSwitch(first, "drop-files"))
            return new ExplorerBridgeCommand(
                ExplorerBridgeCommandKind.SendToFiles,
                ExistingPaths(args.Skip(1)));

        if (IsSwitch(first, "install-sendto"))
            return new ExplorerBridgeCommand(
                ExplorerBridgeCommandKind.InstallSendTo,
                [],
                args.Skip(1).FirstOrDefault(IsNonSwitch));

        if (IsSwitch(first, "uninstall-sendto"))
            return new ExplorerBridgeCommand(ExplorerBridgeCommandKind.UninstallSendTo, []);

        if (IsSwitch(first, "smoke-test"))
            return ParseSmokeCommand(ExplorerBridgeCommandKind.SmokeTest, args);

        if (IsSwitch(first, "smoke-send"))
            return ParseSmokeCommand(ExplorerBridgeCommandKind.SmokeSendFiles, args);

        return new ExplorerBridgeCommand(ExplorerBridgeCommandKind.None, []);
    }

    private static ExplorerBridgeCommand ParseSmokeCommand(
        ExplorerBridgeCommandKind kind,
        IReadOnlyList<string> args)
    {
        var smokePaths = args.Skip(1).ToArray();
        if (smokePaths.Length != 2
            || !Path.IsPathFullyQualified(smokePaths[0])
            || !Path.IsPathFullyQualified(smokePaths[1])
            || !Directory.Exists(smokePaths[0])
            || !File.Exists(smokePaths[1]))
            return new ExplorerBridgeCommand(ExplorerBridgeCommandKind.Invalid, []);

        var profile = Path.GetFullPath(smokePaths[0]);
        var probe = Path.GetFullPath(smokePaths[1]);
        return new ExplorerBridgeCommand(
            kind,
            kind == ExplorerBridgeCommandKind.SmokeSendFiles ? [probe] : [],
            SmokeProfileRoot: profile,
            SmokeProbePath: probe);
    }

    private static bool IsSwitch(string arg, string name) =>
        arg.Equals("--" + name, StringComparison.OrdinalIgnoreCase)
        || arg.Equals("/" + name, StringComparison.OrdinalIgnoreCase);

    private static bool IsNonSwitch(string arg) =>
        !arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith('/');

    private static string[] ExistingPaths(IEnumerable<string> paths) =>
        paths.Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
