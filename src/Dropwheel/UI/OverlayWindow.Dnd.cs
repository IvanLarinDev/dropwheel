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
        if (e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey)) return DropAction.Move;
        if (t.Override != DropAction.Inherit) return t.Override;
        return TargetStore.Config.GlobalAction;
    }

    private void OnBubbleDragOver(TargetItem t, Border badge, DragEventArgs e)
    {
        bool real = e.Data.GetDataPresent(DataFormats.FileDrop);
        bool virt = !real && VirtualFileService.HasVirtualFiles(e.Data);
        bool text = !real && !virt && TextDropService.HasText(e.Data);

        if (real && LaunchService.IsRunTarget(t)) // drop files on an exe/script → run it (open with)
        {
            e.Effects = DragDropEffects.Link;
            ((TextBlock)badge.Child).Text = "▶";
            badge.Background = Brushes.CornflowerBlue;
            badge.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }
        if ((!real && !virt && !text) || !LaunchService.IsFolderTarget(t))
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        var act = virt || text ? DropAction.Copy : Resolve(t, e); // virtual files and text: copy only
        e.Effects = act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        ((TextBlock)badge.Child).Text = t.IsSorter ? "⇅" : text ? "≡" : act == DropAction.Move ? "➜" : "⧉";
        badge.Background = act == DropAction.Move ? Brushes.Orange : Brushes.MediumSpringGreen;
        badge.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnBubbleDrop(TargetItem t, Border badge, DragEventArgs e)
    {
        badge.Visibility = Visibility.Collapsed;
        try { OnBubbleDropCore(t, e); }
        catch (Exception ex)
        {
            ErrorLog.Write($"Error dropping onto '{t.Name}'", ex);
            ShowToast("The operation could not be completed");
        }
        CloseCloud();
        e.Handled = true;
    }

    private void OnBubbleDropCore(TargetItem t, DragEventArgs e)
    {
        // A shortcut target (.lnk) to a folder stores the shortcut's own path — resolve it so files
        // land in the target folder, not next to the .lnk.
        var dest = LaunchService.DestPath(t);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            if (t.IsSorter)
            {
                DropSorted(t, files, Resolve(t, e));
                return;
            }
            if (LaunchService.IsRunTarget(t))
            {
                bool launched = LaunchService.LaunchWith(t, files);
                ShowToast(launched
                    ? $"▶ Opened {files.Length} item(s) with {t.Name}"
                    : "Could not launch");
                return;
            }
            var act = Resolve(t, e);
            bool hadCollision = FileOps.HasDestinationCollision(files, dest);
            bool ok = FileOps.Execute(files, dest, act);
            if (ok) RememberOpIfUnambiguous(act, files, dest, hadCollision);
            ShowToast(ok
                ? $"{(act == DropAction.Move ? "➜ Moved" : "⧉ Copied")}: {files.Length} item(s) → {t.Name}"
                : "Operation was not completed", ok);
        }
        else if (VirtualFileService.HasVirtualFiles(e.Data))
        {
            var saved = VirtualFileService.Extract(e.Data, dest);
            if (saved.Length > 0)
            {
                if (t.IsSorter) SortSavedVirtuals(t, saved);
                else RememberOpIfUnambiguous(DropAction.Copy, saved, dest, hadCollision: false);
            }
            ShowToast(saved.Length > 0
                ? $"⧉ Saved: {saved.Length} item(s) → {t.Name}"
                : "Nothing to save", saved.Length > 0);
        }
        else if (TextDropService.HasText(e.Data))
        {
            var saved = TextDropService.SaveFrom(e.Data, dest, DateTime.Now);
            if (saved is { } path)
            {
                if (t.IsSorter) SortSavedVirtuals(t, new[] { path });
                else RememberOpIfUnambiguous(DropAction.Copy, new[] { path }, dest, hadCollision: false);
            }
            ShowToast(saved != null
                ? $"≡ Saved text → {System.IO.Path.GetFileName(saved)}"
                : "No text to save", saved != null);
        }
    }
}
