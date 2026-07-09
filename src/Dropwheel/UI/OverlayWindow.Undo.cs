using System.IO;
using System.Windows.Input;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    internal readonly record struct FileOp(
        DropAction Act,
        string[] Sources,
        string Dest,
        string[] ExistingDestinations);

    // One drop operation may consist of several moves (sorter).
    private readonly List<FileOp> _lastOps = new();

    private void RememberOp(FileOp op)
    { _lastOps.Clear(); _lastOps.Add(op); }

    private void RememberOps(IEnumerable<FileOp> ops)
    { _lastOps.Clear(); _lastOps.AddRange(ops); }

    internal static FileOp BuildOpBefore(DropAction act, string[] sources, string dest) =>
        new(act, sources, dest, FileOps.DestinationConflicts(sources, dest));

    internal static FileOp BuildCreatedCopyOp(string[] sources, string dest) =>
        new(DropAction.Copy, sources, dest, Array.Empty<string>());

    private void OnUndoClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Undo();
    }

    /// <summary>Best-effort: copy → delete the copies (to Recycle Bin), move → move back.
    /// Files renamed by the conflict dialog are not tracked.</summary>
    private void Undo()
    {
        if (_lastOps.Count == 0) return;
        bool ok = true;
        foreach (var op in _lastOps) ok &= UndoOne(op);
        _lastOps.Clear();
        ShowToast(ok ? "↩ Undone" : "Could not undo completely");
    }

    internal static string[] CopyUndoTargets(FileOp op)
    {
        var protectedPaths = new HashSet<string>(
            op.ExistingDestinations.Select(p => IOPath.GetFullPath(p)),
            StringComparer.OrdinalIgnoreCase);
        return op.Sources
            .Select(s => IOPath.Combine(op.Dest, IOPath.GetFileName(s)))
            .Where(p => !protectedPaths.Contains(IOPath.GetFullPath(p)))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToArray();
    }

    internal static bool UndoOne(FileOp op)
    {
        bool ok = op.ExistingDestinations.Length == 0;
        if (op.Act == DropAction.Copy)
        {
            var copies = CopyUndoTargets(op);
            if (copies.Length > 0) ok = FileOps.Delete(copies);
        }
        else
        {
            foreach (var src in op.Sources)
            {
                var dst = IOPath.Combine(op.Dest, IOPath.GetFileName(src));
                if (!op.ExistingDestinations.Contains(dst, StringComparer.OrdinalIgnoreCase)
                    && (File.Exists(dst) || Directory.Exists(dst))
                    && IOPath.GetDirectoryName(src) is { Length: > 0 } dir)
                    ok &= FileOps.Execute(new[] { dst }, dir, DropAction.Move);
            }
        }
        return ok;
    }
}
