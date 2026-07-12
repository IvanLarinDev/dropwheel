using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Priority: modifier (Ctrl/Shift) → target override → global setting. Delegates to the
    /// pure DropDispatch.ResolveAction so the precedence is unit-testable without DragEventArgs.</summary>
    private static DropAction Resolve(TargetItem t, DragEventArgs e)
        => DropDispatch.ResolveAction(
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey),
            t.Override,
            TargetStore.Config.GlobalAction);

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
            ((TextBlock)badge.Child).Text = telegramText ? "Text" : "Copy";
            badge.Background = Palettes.Info;
            badge.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }

        if (real && LaunchService.IsRunTarget(t)) // drop files on an exe/script → run it (open with)
        {
            e.Effects = DragDropEffects.Link;
            ((TextBlock)badge.Child).Text = "Run";
            badge.Background = Palettes.Info;
            badge.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }
        if ((!real && !virt && !link && !text) || !LaunchService.IsFolderTarget(t))
        { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        var act = DropDispatch.EffectiveAction(
            virt || text, // virtual files and text: copy only
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey),
            t.Override,
            TargetStore.Config.GlobalAction);
        e.Effects = link ? AddTargetDropEffect(e) : act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        ((TextBlock)badge.Child).Text = t.IsSorter ? "Sort" : text ? "Text" : act == DropAction.Move ? "Move" : "Copy";
        if (link) ((TextBlock)badge.Child).Text = "Add";
        badge.Background = link ? Palettes.Info : act == DropAction.Move ? Palettes.Warning : Palettes.Success;
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
            ShowToast("The operation could not be completed", kind: ToastKind.Danger);
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
                ShowToast("Nothing to send", kind: ToastKind.Warning);
                return;
            }

            LaunchService.Launch(new TargetItem { Name = t.Name, Path = TelegramDropService.LaunchPathFor(t) });
            TelegramDropService.PasteIntoTelegramWhenReady();
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            ShowToast(result.Kind == TelegramDropKind.Files
                ? $"Copied {result.Count} file(s); pasting in Telegram"
                : "Copied text; pasting in Telegram", kind: ToastKind.Success);
            return;
        }

        // A shortcut target (.lnk) to a folder stores the shortcut's own path — resolve it so files
        // land in the target folder, not next to the .lnk.
        var dest = LaunchService.DestPath(t);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            switch (DropDispatch.ClassifyFileDrop(t.IsSorter, LaunchService.IsRunTarget(t)))
            {
                case FileDropRoute.Sort:
                    DropSorted(t, files, Resolve(t, e));
                    return;
                case FileDropRoute.Run:
                    bool launched = LaunchService.LaunchWith(t, files);
                    ShowToast(launched
                        ? $"Opened {files.Length} item(s) with {t.Name}"
                        : "Could not launch", kind: launched ? ToastKind.Success : ToastKind.Danger);
                    return;
            }
            var act = Resolve(t, e);
            var op = BuildOpBefore(act, files, dest);
            bool ok = FileOps.Execute(files, dest, act);
            if (ok) RememberOp(op);
            ShowToast(ok
                ? $"{(act == DropAction.Move ? "Moved" : "Copied")}: {files.Length} item(s) → {t.Name}"
                : "Operation was not completed", ok, ok ? ToastKind.Success : ToastKind.Danger);
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
                ? $"Saved: {saved.Length} item(s) → {t.Name}"
                : "Nothing to save", saved.Length > 0,
                saved.Length > 0 ? ToastKind.Success : ToastKind.Warning);
        }
        else if (AddTargetsFromDrop(e.Data, _currentGroup))
        {
        }
        else if (LinkTargetService.HasSavedMessagesLabel(e.Data))
        {
            ShowToast("Saved Messages target was not added", kind: ToastKind.Warning);
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
                ? $"Saved text → {System.IO.Path.GetFileName(saved)}"
                : "No text to save", saved != null,
                saved != null ? ToastKind.Success : ToastKind.Warning);
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
