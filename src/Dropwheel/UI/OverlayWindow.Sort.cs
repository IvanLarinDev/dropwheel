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
        var ops = new List<(DropAction, string[], string, bool)>();
        foreach (var (folder, group) in plan)
        {
            Directory.CreateDirectory(folder);
            bool hadCollision = FileOps.HasDestinationCollision(group, folder);
            if (FileOps.Execute(group, folder, act)) ops.Add((act, group.ToArray(), folder, hadCollision));
            else ok = false;
        }
        if (ops.Count > 0) RememberOpsIfUnambiguous(ops);
        ShowToast(ok
            ? $"⇅ Sorted: {files.Length} item(s) → {t.Name}"
            : "Sorting was not completed", ops.Count > 0);
    }

    /// <summary>Virtual files are already saved into the sorter root — distribute
    /// them by the rules. Undo for them means delete (they are copies).</summary>
    private void SortSavedVirtuals(TargetItem t, string[] saved)
    {
        var plan = SortService.Plan(t, saved);
        var ops = new List<(DropAction, string[], string, bool)>();
        string root = IOPath.GetFullPath(t.Path).TrimEnd('\\');
        foreach (var (folder, group) in plan)
        {
            if (IOPath.GetFullPath(folder).TrimEnd('\\') == root)
            { ops.Add((DropAction.Copy, group.ToArray(), folder, false)); continue; }
            Directory.CreateDirectory(folder);
            bool hadCollision = FileOps.HasDestinationCollision(group, folder);
            if (FileOps.Execute(group, folder, DropAction.Move))
                ops.Add((DropAction.Copy, group.ToArray(), folder, hadCollision));
        }
        if (ops.Count > 0) RememberOpsIfUnambiguous(ops);
    }
}
