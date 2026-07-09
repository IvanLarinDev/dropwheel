using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Real files dropped on a sorter target: distribute by the rules.</summary>
    private void DropSorted(TargetItem t, string[] files, DropAction act)
    {
        var plan = SortService.Plan(t, files);
        bool ok = true;
        var ops = new List<FileOp>();
        foreach (var (folder, group) in plan)
        {
            Directory.CreateDirectory(folder);
            var sources = group.ToArray();
            var op = BuildOpBefore(act, sources, folder);
            if (FileOps.Execute(sources, folder, act)) ops.Add(op);
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
        string root = IOPath.GetFullPath(t.Path).TrimEnd('\\');
        foreach (var (folder, group) in plan)
        {
            if (IOPath.GetFullPath(folder).TrimEnd('\\') == root)
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
