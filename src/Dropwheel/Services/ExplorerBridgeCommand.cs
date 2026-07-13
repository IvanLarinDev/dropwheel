using System.IO;

namespace Dropwheel.Services;

public enum ExplorerBridgeCommandKind { None, SendToFiles, InstallSendTo, UninstallSendTo }

public sealed record ExplorerBridgeCommand(
    ExplorerBridgeCommandKind Kind,
    string[] Paths,
    string? AppPath = null)
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

        return new ExplorerBridgeCommand(ExplorerBridgeCommandKind.None, []);
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
