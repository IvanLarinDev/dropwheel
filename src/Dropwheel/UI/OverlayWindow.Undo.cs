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

    /// <summary>A snapshot of a level taken before targets were added, so the add can be undone by
    /// restoring the level to exactly what it was — order and pin positions included. The added
    /// targets simply aren't in the snapshot, so they vanish on restore.</summary>
    internal readonly record struct AddOp(IList<TargetItem> Level, List<(TargetItem Item, int? Position)> Before);

    // One drop operation may consist of several moves (sorter).
    private readonly List<FileOp> _lastOps = new();
    private AddOp? _lastAdd;

    private void RememberOp(FileOp op)
    { _lastOps.Clear(); _lastOps.Add(op); _lastAdd = null; }

    private void RememberOps(IEnumerable<FileOp> ops)
    { _lastOps.Clear(); _lastOps.AddRange(ops); _lastAdd = null; }

    /// <summary>Snapshots a level just before targets are added to it, arming Undo for the add.</summary>
    private void RememberAdd(IList<TargetItem> level)
    {
        _lastOps.Clear();
        _lastAdd = new AddOp(level, level.Select(t => (t, t.TilePosition)).ToList());
    }

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
    /// Files renamed by the conflict dialog are not tracked. Adding a target is undone by
    /// restoring the level snapshot.</summary>
    private void Undo()
    {
        if (_lastAdd is { } add)
        {
            _lastAdd = null;
            RestoreLevel(add);
            TargetStore.Save();
            if (_open) BuildCloud();
            ShowToast("↩ Undone");
            return;
        }

        if (_lastOps.Count == 0) return;
        bool ok = true;
        foreach (var op in _lastOps) ok &= UndoOne(op);
        _lastOps.Clear();
        ShowToast(ok ? "↩ Undone" : "Could not undo completely");
    }

    /// <summary>Rebuilds a level to its snapshot: same items, same order, same pin positions. Any
    /// targets added after the snapshot are absent from it, so restoring drops them.</summary>
    internal static void RestoreLevel(AddOp add)
    {
        add.Level.Clear();
        foreach (var (item, position) in add.Before)
        {
            item.TilePosition = position;
            add.Level.Add(item);
        }
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
