using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Dropping on the orb adds a target. If a group level is open,
    /// the target goes into it; otherwise into the root.</summary>
    private void OnOrbDrop(object sender, DragEventArgs e)
    {
        if (AddTargetsFromDrop(e.Data, _currentGroup)) e.Handled = true;
    }

    /// <summary>A quick drop on a group bubble (before hover-expand fires)
    /// adds the target into the group.</summary>
    private void OnGroupDrop(TargetItem group, DragEventArgs e)
    {
        _groupHover?.Stop();
        if (AddTargetsFromDrop(e.Data, group)) e.Handled = true;
    }

    private void OnAddTargetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAddTarget(e.Data) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool CanAddTarget(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || LinkTargetService.HasLaunchUri(data);

    private bool AddTargetsFromDrop(IDataObject data, TargetItem? group)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            AddTargets(paths.Select(TargetFromPath), group);
            return true;
        }

        if (LinkTargetService.CreateTarget(data) is { } linkTarget)
        {
            AddTargets(new[] { linkTarget }, group);
            return true;
        }

        return false;
    }

    private static TargetItem TargetFromPath(string path)
    {
        // A dropped .lnk becomes a target for what it points at, not the shortcut file.
        var target = ShortcutResolver.Resolve(path);
        // Keep the shortcut's friendly label (e.g. "Visual Studio Code") over the raw target name.
        var name = IOPath.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = IOPath.GetFileNameWithoutExtension(target);
        if (string.IsNullOrEmpty(name)) name = target;
        return new TargetItem { Name = name, Path = target };
    }

    private void AddTargets(IEnumerable<TargetItem> targets, TargetItem? group)
    {
        var items = targets.ToArray();
        if (items.Length == 0) return;

        var list = group?.Children ?? TargetStore.Config.Targets;
        foreach (var item in items) list.Add(item);

        TargetStore.Save();
        ShowToast(group == null
            ? $"Targets added: {items.Length}"
            : $"Added to {group.Name}: {items.Length}");
        if (_open) BuildCloud();
    }
}
