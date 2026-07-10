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
        bool link = !real && !virt && LinkTargetService.HasLaunchUri(e.Data);
        bool text = !real && !virt && !link && TextDropService.HasText(e.Data);

        if (TelegramDropService.CanAccept(t, e.Data))
        {
            var effect = TelegramDropEffect(e);
            if (effect == DragDropEffects.None)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var telegramText = !real && !virt && TextDropService.HasText(e.Data);
            e.Effects = effect;
            ((TextBlock)badge.Child).Text = telegramText ? "≡" : "⧉";
            badge.Background = Brushes.CornflowerBlue;
            badge.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }

        if (real && LaunchService.IsRunTarget(t)) // drop files on an exe/script → run it (open with)
        {
            e.Effects = DragDropEffects.Link;
            ((TextBlock)badge.Child).Text = "▶";
            badge.Background = Brushes.CornflowerBlue;
            badge.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }
        if ((!real && !virt && !link && !text) || !LaunchService.IsFolderTarget(t))
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        var act = virt || text ? DropAction.Copy : Resolve(t, e); // virtual files and text: copy only
        e.Effects = link ? AddTargetDropEffect(e) : act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        ((TextBlock)badge.Child).Text = t.IsSorter ? "⇅" : text ? "≡" : act == DropAction.Move ? "➜" : "⧉";
        if (link) ((TextBlock)badge.Child).Text = "+";
        badge.Background = link ? Brushes.CornflowerBlue : act == DropAction.Move ? Brushes.Orange : Brushes.MediumSpringGreen;
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
        if (TelegramDropService.CanAccept(t, e.Data))
        {
            var result = TelegramDropService.CopyToClipboard(
                e.Data,
                System.IO.Path.Combine(TargetStore.Dir, "telegram-drop"));

            if (result == null)
            {
                ErrorLog.Write(
                    $"Telegram drop had no extractable payload. AllowedEffects={e.AllowedEffects}; Formats={TextDropService.DescribeFormats(e.Data)}");
                ShowToast("Nothing to send");
                return;
            }

            LaunchService.Launch(new TargetItem { Name = t.Name, Path = TelegramDropService.LaunchPathFor(t) });
            TelegramDropService.PasteIntoTelegramWhenReady();
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            ShowToast(result.Kind == TelegramDropKind.Files
                ? $"⧉ Copied {result.Count} file(s); pasting in Telegram"
                : "≡ Copied text; pasting in Telegram");
            return;
        }

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
            var op = BuildOpBefore(act, files, dest);
            bool ok = FileOps.Execute(files, dest, act);
            if (ok) RememberOp(op);
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
                else RememberOp(BuildCreatedCopyOp(saved, dest));
            }
            ShowToast(saved.Length > 0
                ? $"⧉ Saved: {saved.Length} item(s) → {t.Name}"
                : "Nothing to save", saved.Length > 0);
        }
        else if (AddTargetsFromDrop(e.Data, _currentGroup, pinned: false))
        {
        }
        else if (LinkTargetService.HasSavedMessagesLabel(e.Data))
        {
            ShowToast("Saved Messages target was not added");
        }
        else if (TextDropService.HasText(e.Data))
        {
            var saved = TextDropService.SaveFrom(e.Data, dest, DateTime.Now);
            if (saved is { } path)
            {
                if (t.IsSorter) SortSavedVirtuals(t, new[] { path });
                else RememberOp(BuildCreatedCopyOp(new[] { path }, dest));
            }
            ShowToast(saved != null
                ? $"≡ Saved text → {System.IO.Path.GetFileName(saved)}"
                : "No text to save", saved != null);
        }
    }

    private static DragDropEffects TelegramDropEffect(DragEventArgs e)
    {
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Move)) return DragDropEffects.Move;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }
}
