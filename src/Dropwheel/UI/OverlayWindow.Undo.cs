using System.IO;
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
        string[] ExistingDestinations,
        string[]? DestPaths = null);

    /// <summary>A snapshot of a level taken before targets were added, so the add can be undone by
    /// restoring the level to exactly what it was — order and pin positions included. The added
    /// targets simply aren't in the snapshot, so they vanish on restore.</summary>
    internal readonly record struct AddOp(IList<TargetItem> Level, List<(TargetItem Item, int? Position)> Before);

    /// <summary>A deleted target and where it lived, so the delete can be undone by re-inserting it at
    /// the same spot. Reported by the editor for plain targets (and empty groups).</summary>
    internal readonly record struct DeleteOp(IList<TargetItem> List, TargetItem Item, int Index);

    // One drop operation may consist of several moves (sorter).
    private readonly List<FileOp> _lastOps = new();
    private AddOp? _lastAdd;
    private DeleteOp? _lastDelete;

    private void RememberOp(FileOp op)
    { _lastOps.Clear(); _lastOps.Add(op); _lastAdd = null; _lastDelete = null; }

    private void RememberOps(IEnumerable<FileOp> ops)
    { _lastOps.Clear(); _lastOps.AddRange(ops); _lastAdd = null; _lastDelete = null; }

    /// <summary>Snapshots a level just before targets are added to it, arming Undo for the add.</summary>
    private void RememberAdd(IList<TargetItem> level)
    {
        _lastOps.Clear();
        _lastAdd = new AddOp(level, level.Select(t => (t, t.TilePosition)).ToList());
        _lastDelete = null;
    }

    /// <summary>Arms Undo for a target the editor just deleted.</summary>
    private void RememberDelete(DeleteOp op)
    { _lastOps.Clear(); _lastAdd = null; _lastDelete = op; }

    /// <summary>Deletes a target from the wheel and arms Undo for it — used by the broken-target menu's
    /// Remove. Same effect as deleting from the editor, but without opening it.</summary>
    private void RemoveTargetWithUndo(TargetItem t)
    {
        var parent = TargetStore.FindParentGroup(t);
        IList<TargetItem> list = parent?.Children ?? TargetStore.Config.Targets;
        int index = Math.Max(0, list.IndexOf(t));
        TargetStore.DeleteTarget(t);
        TargetStore.Save();
        RememberDelete(new DeleteOp(list, t, index));
        RefreshGroupShortcuts();
        if (_open) BuildCloud();
        ShowToast($"Removed {t.Name}", canUndo: true);
    }

    internal static FileOp BuildOpBefore(DropAction act, string[] sources, string dest) =>
        new(act, sources, dest, FileOps.DestinationConflicts(sources, dest));

    internal static FileOp BuildCreatedCopyOp(string[] sources, string dest) =>
        new(DropAction.Copy, sources, dest, Array.Empty<string>());

    /// <summary>An op for a renamed drop: each source went to its own explicit destination path
    /// (destPaths lines up with sources), so Undo targets those exact paths instead of dest\originalName.
    /// A destination that already existed before the drop is protected from Undo, like the folder case.</summary>
    internal static FileOp BuildRenamedOp(DropAction act, string[] sources, string dest, string[] destPaths)
    {
        var existing = destPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        return new(act, sources, dest, existing, destPaths);
    }

    /// <summary>The file name a target's NameTemplate produces for a source file: the template expanded
    /// with the built-in ${name} tokens, sanitized to a single name, then the source's own extension. An
    /// empty or unfillable template falls back to the source file's own name.</summary>
    internal static string RenamedFileName(string template, string filePath, DateTime now)
    {
        var expanded = SortService.ExpandTemplate(new SortRule { Dest = template }, filePath, now, out bool ok);
        var stem = ok ? SanitizeFileName(expanded) : "";
        if (stem.Length == 0) stem = IOPath.GetFileNameWithoutExtension(filePath);
        return stem + IOPath.GetExtension(filePath);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = IOPath.GetInvalidFileNameChars();
        var chars = value.Where(ch => Array.IndexOf(invalid, ch) < 0).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    /// <summary>Best-effort: copy → delete the copies (to Recycle Bin), move → move back.
    /// Files renamed by the conflict dialog are not tracked. Adding a target is undone by
    /// restoring the level snapshot.</summary>
    private void Undo()
    {
        if (_lastDelete is { } del)
        {
            _lastDelete = null;
            RestoreDelete(del);
            TargetStore.Save();
            RefreshGroupShortcuts();
            if (_open) BuildCloud();
            ShowToast("Undone");
            return;
        }

        if (_lastAdd is { } add)
        {
            _lastAdd = null;
            RestoreLevel(add);
            TargetStore.Save();
            if (_open) BuildCloud();
            ShowToast("Undone");
            return;
        }

        if (_lastOps.Count == 0) return;
        bool ok = true;
        foreach (var op in _lastOps) ok &= UndoOne(op);
        _lastOps.Clear();
        if (ok) ShowToast("Undone");
        else ShowToast("Could not undo completely", kind: ToastKind.Warning);
    }

    /// <summary>Re-inserts a deleted target at its old index, clamped in case the list changed length
    /// meanwhile. Best-effort like the rest of Undo: it restores the tile, not any cached favicon that
    /// deletion removed (that re-fetches on demand).</summary>
    internal static void RestoreDelete(DeleteOp op) =>
        op.List.Insert(Math.Clamp(op.Index, 0, op.List.Count), op.Item);

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
        // A renamed drop knows its exact destination paths; a plain drop keeps the source names in Dest.
        var candidates = op.DestPaths ?? op.Sources.Select(s => IOPath.Combine(op.Dest, IOPath.GetFileName(s)));
        return candidates
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
        else if (op.DestPaths is { } destPaths)
        {
            // Renamed move: put each file back at its original full path (name and folder).
            for (int i = 0; i < op.Sources.Length; i++)
            {
                var dst = destPaths[i];
                if (!op.ExistingDestinations.Contains(dst, StringComparer.OrdinalIgnoreCase)
                    && (File.Exists(dst) || Directory.Exists(dst)))
                    ok &= FileOps.ExecuteTo(new[] { (dst, op.Sources[i]) }, DropAction.Move);
            }
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
