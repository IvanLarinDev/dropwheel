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

    /// <summary>Real files dropped on a sorter target: distribute by the rules.</summary>
    private void DropSorted(TargetItem t, string[] files, DropAction act)
    {
        var plan = SortService.Plan(t, files);
        bool ok = true;
        var ops = new List<FileOp>();
        foreach (var group in ExecutableSorterGroups(plan))
        {
            Directory.CreateDirectory(group.Folder);
            var op = BuildOpBefore(act, group.Sources, group.Folder);
            if (FileOps.Execute(group.Sources, group.Folder, act)) ops.Add(op);
            else ok = false;
        }
        if (ops.Count > 0) RememberOps(ops);
        ShowToast(ok
            ? $"⇅ Sorted: {files.Length} item(s) → {t.Name}"
            : "Sorting was not completed", ops.Count > 0);
    }

    /// <summary>Virtual files are already saved into the sorter root — distribute
    /// them by the rules. Undo for them means delete (they are copies).</summary>
    private void SortSavedVirtuals(TargetItem t, string[] saved)
    {
        var plan = SortService.Plan(t, saved);
        var ops = new List<FileOp>();
        string root = t.Path;
        foreach (var (folder, group) in plan)
        {
            if (SameNormalizedFolder(folder, root))
            { ops.Add(BuildCreatedCopyOp(group.ToArray(), folder)); continue; }
            Directory.CreateDirectory(folder);
            var sources = group.ToArray();
            if (FileOps.Execute(sources, folder, DropAction.Move))
                ops.Add(BuildCreatedCopyOp(sources, folder));
        }
        if (ops.Count > 0) RememberOps(ops);
    }

    private void SortTargetFolderNow(TargetItem t)
    {
        try
        {
            if (!Directory.Exists(t.Path))
            {
                ShowToast("Sorter folder is unavailable");
                return;
            }

            var plan = SortService.MovePlan(t, Directory.GetFiles(t.Path));
            if (plan.Count == 0)
            {
                ShowToast("Nothing to sort");
                return;
            }

            bool ok = true;
            int moved = 0;
            var ops = new List<FileOp>();
            foreach (var (folder, sources) in plan)
            {
                Directory.CreateDirectory(folder);
                var op = BuildOpBefore(DropAction.Move, sources, folder);
                if (FileOps.Execute(sources, folder, DropAction.Move))
                {
                    ops.Add(op);
                    moved += sources.Length;
                }
                else ok = false;
            }

            if (ops.Count > 0) RememberOps(ops);
            ShowToast(ok
                ? $"⇅ Sorted: {moved} item(s) → {t.Name}"
                : "Sorting was not completed", ops.Count > 0);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Manual sort of '{t.Name}' failed", ex);
            ShowToast("Sorting was not completed");
        }
    }
}
