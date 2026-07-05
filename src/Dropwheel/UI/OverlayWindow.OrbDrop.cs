using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Бросок на сам кружок = добавить перетащенное как новую цель (папку/приложение).</summary>
    private void OnOrbDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        foreach (var p in paths)
        {
            var name = IOPath.GetFileNameWithoutExtension(p);
            if (string.IsNullOrEmpty(name)) name = p;
            TargetStore.Config.Targets.Add(new TargetItem { Name = name, Path = p });
        }
        TargetStore.Save();
        ShowToast($"Targets added: {paths.Length}");
        if (_open) BuildCloud();
        e.Handled = true;
    }
}
