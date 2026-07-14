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

    private void OnBubbleDragOver(FrameworkElement tile, TargetItem t, Border badge, DragEventArgs e)
    {
        var preview = PreviewBubbleDrop(t, e);
        e.Effects = preview.Effects;
        ShowDropConfidence(tile, t, badge, preview);
        e.Handled = true;
    }

    private DropConfidencePreview PreviewBubbleDrop(TargetItem t, DragEventArgs e)
    {
        var payload = DetectDropPayloadKind(e.Data);
        bool real = payload == DropPayloadKind.Files;
        bool virt = payload == DropPayloadKind.VirtualFiles;
        bool link = payload == DropPayloadKind.Link;
        bool text = payload == DropPayloadKind.Text;

        if (TelegramDropService.CanAccept(t, e.Data))
        {
            var effect = TelegramDropEffect(e);
            if (effect == DragDropEffects.None)
            {
                return new DropConfidencePreview(
                    DragDropEffects.None, "No", "Can't",
                    "Cannot send this payload to Telegram.",
                    ConfidenceTone.Danger, CanDrop: false,
                    ActiveLabelText: "Cannot send",
                    PayloadKind: payload);
            }

            var telegramText = !real && !virt && TextDropService.HasText(e.Data);
            var telegramAction = telegramText ? "Copy text" : "Copy files";
            return new DropConfidencePreview(
                effect,
                "Clip",
                telegramText ? "Text" : "Files",
                telegramText
                    ? $"Drop to copy text for Telegram handoff to {t.Name}."
                    : $"Drop to copy files for Telegram handoff to {t.Name}.",
                ConfidenceTone.Warning,
                CanDrop: true,
                ActiveLabelText: $"{telegramAction} via clipboard",
                PayloadKind: payload);
        }

        var isFolderTarget = LaunchService.IsFolderTarget(t);
        var isRunTarget = LaunchService.IsRunTarget(t);
        var targetKind = DropIntent.ClassifyTarget(t, isFolderTarget, isRunTarget);
        var compatibility = targetKind == DropTargetKind.Telegram
            ? DropCompatibility.Deny("Cannot send this payload to Telegram.")
            : DropIntent.Compatibility(payload, targetKind);

        if (payload == DropPayloadKind.Files && targetKind == DropTargetKind.Run)
        {
            return new DropConfidencePreview(
                DragDropEffects.Link,
                "Run",
                "Run",
                $"Drop to run {t.Name} with {RealFileCount(e)} item(s).",
                ConfidenceTone.Warning,
                CanDrop: true,
                ActiveLabelText: $"Run with {t.Name}",
                PayloadKind: payload);
        }

        if (!compatibility.CanReceive)
        {
            var reason = targetKind == DropTargetKind.Missing
                ? $"{t.Name} is missing. Locate or remove this target."
                : compatibility.Reason;
            return new DropConfidencePreview(
                DragDropEffects.None,
                "No",
                "Can't",
                reason,
                ConfidenceTone.Danger,
                CanDrop: false,
                ActiveLabelText: targetKind == DropTargetKind.Missing ? "Target missing" : "Cannot receive",
                PayloadKind: payload);
        }

        var act = DropDispatch.EffectiveAction(
            virt || text, // virtual files and text: copy only
            e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey),
            t.Override,
            TargetStore.Config.GlobalAction);
        if (link)
        {
            var effect = AddTargetDropEffect(e);
            return new DropConfidencePreview(
                effect,
                effect == DragDropEffects.None ? "No" : "Add",
                effect == DragDropEffects.None ? "Can't" : "Add",
                effect == DragDropEffects.None
                    ? "This link cannot be added from the current drag source."
                    : $"Drop to add this link to the current wheel level.",
                effect == DragDropEffects.None ? ConfidenceTone.Danger : ConfidenceTone.Info,
                CanDrop: effect != DragDropEffects.None,
                ActiveLabelText: effect == DragDropEffects.None ? "Cannot add" : "Add link",
                PayloadKind: payload);
        }

        if (targetKind == DropTargetKind.Sorter)
        {
            var badgeText = t.Watch ? "Watch" : "Sort";
            var chipText = t.Watch ? "Watch" : "Rules";
            return new DropConfidencePreview(
                act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy,
                badgeText,
                chipText,
                t.Watch
                    ? $"Drop to route through watched sorter rules in {t.Name}."
                    : $"Drop to route by rules into {t.Name}.",
                ConfidenceTone.Warning,
                CanDrop: true,
                ActiveLabelText: t.Watch ? $"Watch rules in {t.Name}" : $"Rules to {t.Name}",
                PayloadKind: payload);
        }

        if (text)
        {
            return new DropConfidencePreview(
                DragDropEffects.Copy,
                "Text",
                "Text",
                $"Drop to save text in {t.Name}.",
                ConfidenceTone.Info,
                CanDrop: true,
                ActiveLabelText: $"Save text in {t.Name}",
                PayloadKind: payload);
        }

        return new DropConfidencePreview(
            act == DropAction.Move ? DragDropEffects.Move : DragDropEffects.Copy,
            act == DropAction.Move ? "Move" : "Copy",
            act == DropAction.Move ? "Move" : "Copy",
            act == DropAction.Move
                ? $"Drop to move to {t.Name}."
                : $"Drop to copy to {t.Name}.",
            act == DropAction.Move ? ConfidenceTone.Warning : ConfidenceTone.Success,
            CanDrop: true,
            ActiveLabelText: act == DropAction.Move ? $"Move to {t.Name}" : $"Copy to {t.Name}",
            PayloadKind: payload);
    }

    private static DropPayloadKind DetectDropPayloadKind(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop)) return DropPayloadKind.Files;
        if (VirtualFileService.HasVirtualFiles(data)) return DropPayloadKind.VirtualFiles;
        if (LinkTargetService.HasLaunchUri(data)) return DropPayloadKind.Link;
        return TextDropService.HasText(data) ? DropPayloadKind.Text : DropPayloadKind.Unsupported;
    }

    private static int RealFileCount(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files ? files.Length : 0;

    private void OnBubbleDrop(TargetItem t, Border badge, DragEventArgs e)
    {
        badge.Visibility = Visibility.Collapsed;
        try
        {
            if (ConfirmDropPreflight(t, e)) OnBubbleDropCore(t, e);
            else e.Effects = DragDropEffects.None;
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Error dropping onto '{t.Name}'", ex);
            ShowToast("The operation could not be completed", kind: ToastKind.Danger);
        }
        CloseCloud();
        e.Handled = true;
    }

    private bool ConfirmDropPreflight(TargetItem t, DragEventArgs e)
    {
        var payload = DetectDropPayloadKind(e.Data);
        var targetKind = TelegramDropService.CanAccept(t, e.Data)
            ? DropTargetKind.Telegram
            : DropIntent.ClassifyTarget(t, LaunchService.IsFolderTarget(t), LaunchService.IsRunTarget(t));

        return ConfirmDropPreflight(t, payload, targetKind, DropPayloadItemCount(e.Data, payload));
    }

    private bool ConfirmDropPreflight(
        TargetItem t,
        DropPayloadKind payload,
        DropTargetKind targetKind,
        int itemCount)
    {
        var preflight = DropTrustGate.Evaluate(t, payload, targetKind, itemCount);
        return preflight == null
            || DwMessageBox.Show(
                this,
                preflight.Value.Caption,
                preflight.Value.Message,
                preflight.Value.PrimaryText,
                showCancel: true);
    }

    private static int DropPayloadItemCount(IDataObject data, DropPayloadKind payload) =>
        payload switch
        {
            DropPayloadKind.Files when data.GetData(DataFormats.FileDrop) is string[] files => files.Length,
            DropPayloadKind.Link or DropPayloadKind.Text => 1,
            _ => 0,
        };

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
            var historyPayload = result.Kind == TelegramDropKind.Files ? DropPayloadKind.Files : DropPayloadKind.Text;
            TelegramDropService.PasteIntoTelegramWhenReady(pasted =>
            {
                RememberDropHistory(
                    DropHistoryAction.Telegram,
                    t,
                    historyPayload,
                    result.Count,
                    pasted ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                    detail: pasted
                        ? "Pasted via clipboard handoff."
                        : "Telegram did not become ready; payload left on clipboard.");
                if (!pasted)
                {
                    _ = Dispatcher.InvokeAsync(() => ShowToast(
                        "Telegram did not become ready. The payload is still on the clipboard.",
                        kind: ToastKind.Warning));
                }
            });
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            ShowToast(result.Kind == TelegramDropKind.Files
                ? $"Copied {result.Count} file(s); waiting for Telegram"
                : "Copied text; waiting for Telegram", kind: ToastKind.Info);
            return;
        }

        // A shortcut target (.lnk) to a folder stores the shortcut's own path — resolve it so files
        // land in the target folder, not next to the .lnk.
        var dest = LaunchService.DestPath(t);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            switch (DropDispatch.ClassifyFileDrop(t.IsSorter && !DropDispatch.SortingPaused, LaunchService.IsRunTarget(t)))
            {
                case FileDropRoute.Sort:
                    DropSorted(t, files, Resolve(t, e));
                    return;
                case FileDropRoute.Run:
                    bool launched = LaunchService.LaunchWith(t, files);
                    RememberDropHistory(
                        DropHistoryAction.Run,
                        t,
                        DropPayloadKind.Files,
                        files.Length,
                        launched ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                        detail: launched ? null : "LaunchWith returned false.");
                    ShowToast(launched
                        ? $"Opened {files.Length} item(s) with {t.Name}"
                        : "Could not launch", kind: launched ? ToastKind.Success : ToastKind.Danger);
                    return;
            }
            var act = Resolve(t, e);
            FileOp op;
            bool ok;
            var historyDest = dest;
            int skipped = 0;
            if (!string.IsNullOrWhiteSpace(t.NameTemplate))
            {
                var now = DateTime.Now;
                var pairs = files
                    .Select(f => (Source: f, Dest: System.IO.Path.Combine(dest, RenamedFileName(t.NameTemplate!, f, now))))
                    .ToList();
                if (t.ConflictPolicy == ConflictPolicy.Skip)
                {
                    int before = pairs.Count;
                    pairs = pairs.Where(p => !(System.IO.File.Exists(p.Dest) || System.IO.Directory.Exists(p.Dest))).ToList();
                    skipped = before - pairs.Count;
                }
                var srcs = pairs.Select(p => p.Source).ToArray();
                var destPaths = pairs.Select(p => p.Dest).ToArray();
                op = BuildRenamedOp(act, srcs, dest, destPaths);
                ok = FileOps.ExecuteTo(pairs, act, policy: t.ConflictPolicy);
                if (destPaths.Length == 1) historyDest = destPaths[0];
            }
            else
            {
                var toCopy = files;
                if (t.ConflictPolicy == ConflictPolicy.Skip)
                {
                    var conflictNames = FileOps.DestinationConflicts(files, dest)
                        .Select(System.IO.Path.GetFileName)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    toCopy = files.Where(f => !conflictNames.Contains(System.IO.Path.GetFileName(f))).ToArray();
                    skipped = files.Length - toCopy.Length;
                }
                op = BuildOpBefore(act, toCopy, dest);
                ok = FileOps.Execute(toCopy, dest, act, policy: t.ConflictPolicy);
            }
            if (ok) RememberOp(op);
            RememberDropHistory(
                act == DropAction.Move ? DropHistoryAction.Move : DropHistoryAction.Copy,
                t,
                DropPayloadKind.Files,
                files.Length,
                ok ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                destination: historyDest);
            var verb = act == DropAction.Move ? "Moved" : "Copied";
            int moved = files.Length - skipped;
            ShowToast(ok
                ? (skipped == 0 ? $"{verb}: {files.Length} item(s) → {t.Name}"
                    : moved == 0 ? $"All {skipped} already at {t.Name} — skipped"
                    : $"{verb}: {moved} → {t.Name}, {skipped} skipped")
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
            if (!t.IsSorter)
            {
                RememberDropHistory(
                    DropHistoryAction.SaveVirtualFiles,
                    t,
                    DropPayloadKind.VirtualFiles,
                    saved.Length,
                    saved.Length > 0 ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                    destination: saved.Length == 1 ? saved[0] : dest,
                    detail: saved.Length > 0 ? null : "No virtual files were extracted.");
            }
            ShowToast(saved.Length > 0
                ? $"Saved: {saved.Length} item(s) → {t.Name}"
                : "Nothing to save", saved.Length > 0,
                saved.Length > 0 ? ToastKind.Success : ToastKind.Warning);
        }
        // Selected text (a browser attaches the page URL to it) must save as a file before the link
        // branch, or a text selection dropped on a folder would be mistaken for a link tile.
        else if (LinkTargetService.HasSelectedText(e.Data))
        {
            SaveDroppedText(t, dest, e.Data);
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
            SaveDroppedText(t, dest, e.Data);
        }
    }

    /// <summary>Saves dropped text into the target folder (or routes it through a sorter), records the
    /// drop, and reports the result. Shared by the selected-text and plain-text drop branches.</summary>
    private void SaveDroppedText(TargetItem t, string dest, IDataObject data)
    {
        var saved = TextDropService.SaveFrom(data, dest, DateTime.Now, TargetStore.Config.TextFileNameTemplate);
        if (saved is { } path)
        {
            if (t.IsSorter) SortSavedVirtuals(t, new[] { path });
            else RememberOp(BuildCreatedCopyOp(new[] { path }, dest));
        }
        if (!t.IsSorter)
        {
            RememberDropHistory(
                DropHistoryAction.SaveText,
                t,
                DropPayloadKind.Text,
                saved != null ? 1 : 0,
                saved != null ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                destination: saved ?? dest,
                detail: saved != null ? null : "No text was saved.");
        }
        ShowToast(saved != null
            ? $"Saved text → {System.IO.Path.GetFileName(saved)}"
            : "No text to save", saved != null,
            saved != null ? ToastKind.Success : ToastKind.Warning);
    }

    private static DragDropEffects TelegramDropEffect(DragEventArgs e)
    {
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Move)) return DragDropEffects.Move;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }
}
