using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    internal readonly record struct SorterExecutionGroup(string Folder, string[] Sources);

    internal static bool SameNormalizedFolder(string left, string right) =>
        string.Equals(
            IOPath.TrimEndingDirectorySeparator(IOPath.GetFullPath(left)),
            IOPath.TrimEndingDirectorySeparator(IOPath.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<SorterExecutionGroup> ExecutableSorterGroups(
        Dictionary<string, List<string>> plan)
    {
        var groups = new List<SorterExecutionGroup>();
        foreach (var (folder, group) in plan)
        {
            var sources = group
                .Where(source => !WatcherService.SameFolder(folder, source))
                .ToArray();
            if (sources.Length > 0) groups.Add(new SorterExecutionGroup(folder, sources));
        }
        return groups;
    }

    /// <summary>The success toast for a sorter run. Once the batch fanned out into several folders, the
    /// toast says how many, so the result reads at a glance ("Sorted: 12 item(s) → Video · 3 folders").
    /// A single folder is not worth the extra words.</summary>
    internal static string SortedToastText(int items, string targetName, int folders) =>
        folders > 1
            ? $"Sorted: {items} item(s) → {targetName} · {folders} folders"
            : $"Sorted: {items} item(s) → {targetName}";

    /// <summary>Real files dropped on a sorter target: distribute by the rules.</summary>
    private void DropSorted(TargetItem t, string[] files, DropAction act)
    {
        var plan = SortService.Plan(t, files);
        var ops = new List<FileOp>();
        var completed = 0;
        var incomplete = false;
        foreach (var group in ExecutableSorterGroups(plan))
        {
            Directory.CreateDirectory(group.Folder);
            var op = BuildOpBefore(act, group.Sources, group.Folder);
            var outcome = FileOps.ExecuteDetailed(group.Sources, group.Folder, act);
            if (outcome.Status == FileOperationStatus.Succeeded)
            {
                ops.Add(op);
                completed += outcome.CompletedCount;
            }
            else
            {
                incomplete = true;
                completed += outcome.CompletedCount;
                if (outcome.UndoableChanges.Count > 0)
                    ops.Add(BuildPartialOp(act, outcome.UndoableChanges));
            }
        }
        if (ops.Count > 0) RememberOps(ops);
        var status = incomplete
            ? completed > 0 ? DropHistoryStatus.PartiallySucceeded : DropHistoryStatus.Failed
            : DropHistoryStatus.Succeeded;
        RememberDropHistory(
            DropHistoryAction.Sort,
            t,
            DropPayloadKind.Files,
            status == DropHistoryStatus.Succeeded ? files.Length : completed,
            status,
            destination: t.Path,
            detail: status == DropHistoryStatus.Succeeded
                ? ops.Count == 0 ? "No file moves were needed."
                    : ops.Count > 1 ? $"Routed into {ops.Count} folders." : null
                : status == DropHistoryStatus.PartiallySucceeded
                    ? $"Routed {completed} of {files.Length} items before the operation stopped."
                    : "No sorter route completed.");
        ShowToast(status switch
        {
            DropHistoryStatus.Succeeded => SortedToastText(files.Length, t.Name, ops.Count),
            DropHistoryStatus.PartiallySucceeded => $"Sorted partially: {completed} of {files.Length} item(s)",
            _ => "Sorting was not completed",
        }, canUndo: ops.Count > 0, kind: status switch
        {
            DropHistoryStatus.Succeeded => ToastKind.Success,
            DropHistoryStatus.PartiallySucceeded => ToastKind.Warning,
            _ => ToastKind.Danger,
        });
    }

    /// <summary>Virtual files are already saved into the sorter root — distribute
    /// them by the rules. Undo for them means delete (they are copies).</summary>
    private void SortSavedVirtuals(TargetItem t, string[] saved)
    {
        var plan = SortService.Plan(t, saved);
        var ops = new List<FileOp>();
        string root = t.Path;
        var completed = 0;
        var incomplete = false;
        foreach (var (folder, group) in plan)
        {
            if (SameNormalizedFolder(folder, root))
            {
                ops.Add(BuildCreatedCopyOp(group.ToArray(), folder));
                completed += group.Count;
                continue;
            }
            Directory.CreateDirectory(folder);
            var sources = group.ToArray();
            var outcome = FileOps.ExecuteDetailed(sources, folder, DropAction.Move);
            if (outcome.Status == FileOperationStatus.Succeeded)
            {
                ops.Add(BuildCreatedCopyOp(sources, folder));
                completed += outcome.CompletedCount;
            }
            else
            {
                incomplete = true;
                completed += outcome.CompletedCount;
                if (outcome.UndoableChanges.Count > 0)
                    ops.Add(BuildPartialOp(DropAction.Copy, outcome.UndoableChanges));
            }
        }
        if (ops.Count > 0) RememberOps(ops);
        var status = incomplete
            ? completed > 0 ? DropHistoryStatus.PartiallySucceeded : DropHistoryStatus.Failed
            : saved.Length > 0 ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed;
        RememberDropHistory(
            DropHistoryAction.Sort,
            t,
            DropPayloadKind.VirtualFiles,
            status == DropHistoryStatus.Succeeded ? saved.Length : completed,
            status,
            destination: t.Path,
            detail: status switch
            {
                DropHistoryStatus.Succeeded => null,
                DropHistoryStatus.PartiallySucceeded => $"Routed {completed} of {saved.Length} saved items.",
                _ => "No saved virtual files were routed.",
            });
    }

    private void SortTargetFolderNow(TargetItem t)
    {
        try
        {
            if (!Directory.Exists(t.Path))
            {
                ShowToast("Sorter folder is unavailable", kind: ToastKind.Warning);
                return;
            }

            var plan = SortService.MovePlan(t, Directory.GetFileSystemEntries(t.Path));
            if (plan.Count == 0)
            {
                ShowToast("Nothing to sort", kind: ToastKind.Info);
                return;
            }

            int moved = 0;
            bool incomplete = false;
            var ops = new List<FileOp>();
            foreach (var (folder, sources) in plan)
            {
                Directory.CreateDirectory(folder);
                var op = BuildOpBefore(DropAction.Move, sources, folder);
                var outcome = FileOps.ExecuteDetailed(sources, folder, DropAction.Move);
                if (outcome.Status == FileOperationStatus.Succeeded)
                {
                    ops.Add(op);
                    moved += outcome.CompletedCount;
                }
                else
                {
                    incomplete = true;
                    moved += outcome.CompletedCount;
                    if (outcome.UndoableChanges.Count > 0)
                        ops.Add(BuildPartialOp(DropAction.Move, outcome.UndoableChanges));
                }
            }

            if (ops.Count > 0) RememberOps(ops);
            var status = incomplete
                ? moved > 0 ? DropHistoryStatus.PartiallySucceeded : DropHistoryStatus.Failed
                : DropHistoryStatus.Succeeded;
            RememberDropHistory(
                DropHistoryAction.Sort,
                t,
                DropPayloadKind.Files,
                moved,
                status,
                destination: t.Path,
                detail: status == DropHistoryStatus.Succeeded
                    ? ops.Count > 1 ? $"Manual sorter run, into {ops.Count} folders." : "Manual sorter run."
                    : status == DropHistoryStatus.PartiallySucceeded
                        ? $"Manual sorter moved {moved} items before stopping."
                        : "Manual sorter run failed.");
            ShowToast(status switch
            {
                DropHistoryStatus.Succeeded => SortedToastText(moved, t.Name, ops.Count),
                DropHistoryStatus.PartiallySucceeded => $"Sorted partially: {moved} item(s)",
                _ => "Sorting was not completed",
            }, canUndo: ops.Count > 0, kind: status switch
            {
                DropHistoryStatus.Succeeded => ToastKind.Success,
                DropHistoryStatus.PartiallySucceeded => ToastKind.Warning,
                _ => ToastKind.Danger,
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Manual sort of '{t.Name}' failed", ex);
            ShowToast("Sorting was not completed", kind: ToastKind.Danger);
        }
    }
}
