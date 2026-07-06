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
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        AddTargets(paths, _currentGroup);
        e.Handled = true;
    }

    /// <summary>A quick drop on a group bubble (before hover-expand fires)
    /// adds the target into the group.</summary>
    private void OnGroupDrop(TargetItem group, DragEventArgs e)
    {
        _groupHover?.Stop();
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        AddTargets(paths, group);
        e.Handled = true;
    }

    private void AddTargets(string[] paths, TargetItem? group)
    {
        var list = group?.Children ?? TargetStore.Config.Targets;
        foreach (var p in paths)
        {
            var name = IOPath.GetFileNameWithoutExtension(p);
            if (string.IsNullOrEmpty(name)) name = p;
            list.Add(new TargetItem { Name = name, Path = p });
        }
        TargetStore.Save();
        ShowToast(group == null
            ? $"Targets added: {paths.Length}"
            : $"Added to {group.Name}: {paths.Length}");
        if (_open) BuildCloud();
    }
}
