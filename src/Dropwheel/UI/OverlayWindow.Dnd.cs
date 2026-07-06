using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Priority: modifier (Ctrl/Shift) → target override → global setting.</summary>
    private static DropAction Resolve(TargetItem t, DragEventArgs e)
    {
        if (e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)) return DropAction.Copy;
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))   return DropAction.Move;
        if (t.Override != DropAction.Inherit) return t.Override;
        return TargetStore.Config.GlobalAction;
    }

    private void OnBubbleDragOver(TargetItem t, Border badge, DragEventArgs e)
    {
        bool real = e.Data.GetDataPresent(DataFormats.FileDrop);
        bool virt = !real && VirtualFileService.HasVirtualFiles(e.Data);
        if ((!real && !virt) || !t.IsFolder)
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        var act = virt ? DropAction.Copy : Resolve(t, e); // virtual files: copy only
        e.Effects = act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        ((TextBlock)badge.Child).Text = t.IsSorter ? "⇅" : act == DropAction.Move ? "➜" : "⧉";
        badge.Background = act == DropAction.Move ? Brushes.Orange : Brushes.MediumSpringGreen;
        badge.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnBubbleDrop(TargetItem t, Border badge, DragEventArgs e)
    {
        badge.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (t.IsSorter)
            {
                DropSorted(t, files, Resolve(t, e));
                CloseCloud();
                e.Handled = true;
                return;
            }
            var act = Resolve(t, e);
            bool ok = FileOps.Execute(files, t.Path, act);
            if (ok) RememberOp(act, files, t.Path);
            ShowToast(ok
                ? $"{(act == DropAction.Move ? "➜ Moved" : "⧉ Copied")}: {files.Length} item(s) → {t.Name}"
                : "Operation was not completed", ok);
        }
        else if (VirtualFileService.HasVirtualFiles(e.Data))
        {
            var saved = VirtualFileService.Extract(e.Data, t.Path);
            if (saved.Length > 0)
            {
                if (t.IsSorter) SortSavedVirtuals(t, saved);
                else RememberOp(DropAction.Copy, saved, t.Path);
            }
            ShowToast(saved.Length > 0
                ? $"⧉ Saved: {saved.Length} item(s) → {t.Name}"
                : "Nothing to save", saved.Length > 0);
        }
        CloseCloud();
        e.Handled = true;
    }
}
