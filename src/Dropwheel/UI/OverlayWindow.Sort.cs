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

    internal static string[] ApplyConflictPolicy(string[] sources, string folder, ConflictPolicy policy)
    {
        if (policy == ConflictPolicy.KeepBoth) return sources;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return sources.Where(source =>
        {
            var destination = IOPath.GetFullPath(IOPath.Combine(
                folder,
                IOPath.GetFileName(IOPath.TrimEndingDirectorySeparator(source))));
            if (!destinations.Add(destination)) return false;
            return policy != ConflictPolicy.Skip
                || (!File.Exists(destination) && !Directory.Exists(destination));
        }).ToArray();
    }

    /// <summary>The success toast for a sorter run. Once the batch fanned out into several folders, the
    /// toast says how many, so the result reads at a glance ("Sorted: 12 item(s) → Video · 3 folders").
    /// A single folder is not worth the extra words.</summary>
    internal static string SortedToastText(int items, string targetName, int folders, int skipped = 0)
    {
        var details = new List<string>();
        if (folders > 1) details.Add($"{folders} folders");
        if (skipped > 0) details.Add($"{skipped} skipped");
        return $"Sorted: {items} item(s) → {targetName}"
            + (details.Count > 0 ? " · " + string.Join(" · ", details) : "");
    }

    /// <summary>Real files dropped on a sorter target: distribute by the rules.</summary>
    private void DropSorted(TargetItem t, string[] files, DropAction act)
    {
        var plan = SortService.Plan(t, files);
        var ops = new List<FileOp>();
        var executionGroups = ExecutableSorterGroups(plan);
        var completed = files.Length - executionGroups.Sum(group => group.Sources.Length);
        var skipped = 0;
        var routedFolders = 0;
        var incomplete = false;
        foreach (var group in executionGroups)
        {
            Directory.CreateDirectory(group.Folder);
            var sources = ApplyConflictPolicy(group.Sources, group.Folder, t.ConflictPolicy);
            skipped += group.Sources.Length - sources.Length;
            if (sources.Length == 0) continue;
            var outcome = FileOps.ExecuteDetailed(sources, group.Folder, act, policy: t.ConflictPolicy);
            if (outcome.CompletedCount > 0) routedFolders++;
            if (outcome.Status == FileOperationStatus.Succeeded)
            {
                var actual = BuildPartialOp(act, outcome.UndoableChanges);
                if (CanUndo(actual)) ops.Add(actual);
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
        var undoableOps = ops.Where(CanUndo).ToArray();
        if (undoableOps.Length > 0) RememberOps(undoableOps);
        var status = incomplete
            ? completed > 0 ? DropHistoryStatus.PartiallySucceeded : DropHistoryStatus.Failed
            : DropHistoryStatus.Succeeded;
        RememberDropHistory(
            DropHistoryAction.Sort,
            t,
            DropPayloadKind.Files,
            completed,
            status,
            destination: t.Path,
            detail: status == DropHistoryStatus.Succeeded
                ? skipped > 0 ? $"Routed {completed} item(s); skipped {skipped} colliding destination(s)."
                    : routedFolders == 0 ? "No file moves were needed."
                    : routedFolders > 1 ? $"Routed into {routedFolders} folders." : null
                : status == DropHistoryStatus.PartiallySucceeded
                    ? $"Routed {completed} of {files.Length} items before the operation stopped."
                    : "No sorter route completed.");
        ShowToast(status switch
        {
            DropHistoryStatus.Succeeded => SortedToastText(completed, t.Name, routedFolders, skipped),
            DropHistoryStatus.PartiallySucceeded => $"Sorted partially: {completed} of {files.Length} item(s)",
            _ => "Sorting was not completed",
        }, canUndo: undoableOps.Length > 0, kind: status switch
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
        var skipped = 0;
        var incomplete = false;
        foreach (var (folder, group) in plan)
        {
            if (SameNormalizedFolder(folder, root))
            {
                var actual = CaptureUndoState(BuildCreatedCopyOp(group.ToArray(), folder));
                if (CanUndo(actual)) ops.Add(actual);
                completed += group.Count;
                continue;
            }
            Directory.CreateDirectory(folder);
            var plannedSources = group.ToArray();
            var sources = ApplyConflictPolicy(plannedSources, folder, t.ConflictPolicy);
            var skippedHere = plannedSources.Length - sources.Length;
            skipped += skippedHere;
            if (skippedHere > 0)
            {
                var selected = sources.Select(IOPath.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var leftInRoot = plannedSources
                    .Where(source => !selected.Contains(IOPath.GetFullPath(source)))
                    .ToArray();
                var created = CaptureUndoState(BuildCreatedCopyOp(leftInRoot, root));
                if (CanUndo(created)) ops.Add(created);
            }
            if (sources.Length == 0) continue;
            var outcome = FileOps.ExecuteDetailed(sources, folder, DropAction.Move, policy: t.ConflictPolicy);
            if (outcome.Status == FileOperationStatus.Succeeded)
            {
                var actual = BuildPartialOp(DropAction.Copy, outcome.UndoableChanges);
                if (CanUndo(actual)) ops.Add(actual);
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
        var undoableOps = ops.Where(CanUndo).ToArray();
        if (undoableOps.Length > 0) RememberOps(undoableOps);
        var status = incomplete
            ? completed > 0 ? DropHistoryStatus.PartiallySucceeded : DropHistoryStatus.Failed
            : saved.Length > 0 ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed;
        RememberDropHistory(
            DropHistoryAction.Sort,
            t,
            DropPayloadKind.VirtualFiles,
            completed,
            status,
            destination: t.Path,
            detail: status switch
            {
                DropHistoryStatus.Succeeded when skipped > 0 =>
                    $"Routed {completed} saved item(s); skipped {skipped} colliding destination(s).",
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
            int skipped = 0;
            int routedFolders = 0;
            bool incomplete = false;
            var ops = new List<FileOp>();
            foreach (var (folder, sources) in plan)
            {
                Directory.CreateDirectory(folder);
                var candidates = ApplyConflictPolicy(sources, folder, t.ConflictPolicy);
                skipped += sources.Length - candidates.Length;
                if (candidates.Length == 0) continue;
                var outcome = FileOps.ExecuteDetailed(candidates, folder, DropAction.Move, policy: t.ConflictPolicy);
                if (outcome.CompletedCount > 0) routedFolders++;
                if (outcome.Status == FileOperationStatus.Succeeded)
                {
                    var actual = BuildPartialOp(DropAction.Move, outcome.UndoableChanges);
                    if (CanUndo(actual)) ops.Add(actual);
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

            var undoableOps = ops.Where(CanUndo).ToArray();
            if (undoableOps.Length > 0) RememberOps(undoableOps);
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
                    ? skipped > 0 ? $"Manual sorter moved {moved} item(s); skipped {skipped} colliding destination(s)."
                    : routedFolders > 1 ? $"Manual sorter run, into {routedFolders} folders." : "Manual sorter run."
                    : status == DropHistoryStatus.PartiallySucceeded
                        ? $"Manual sorter moved {moved} items before stopping."
                        : "Manual sorter run failed.");
            ShowToast(status switch
            {
                DropHistoryStatus.Succeeded => SortedToastText(moved, t.Name, routedFolders, skipped),
                DropHistoryStatus.PartiallySucceeded => $"Sorted partially: {moved} item(s)",
                _ => "Sorting was not completed",
            }, canUndo: undoableOps.Length > 0, kind: status switch
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
