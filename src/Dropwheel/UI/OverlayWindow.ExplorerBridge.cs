using System.IO;
using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using WpfDataObject = System.Windows.DataObject;
using WpfDataFormats = System.Windows.DataFormats;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private string[]? _explorerBridgeFiles;

    public void OpenFromExplorerFiles(IEnumerable<string> paths)
    {
        var files = paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            ShowToast("Explorer did not pass any existing files", kind: ToastKind.Warning);
            return;
        }

        _explorerBridgeFiles = files;
        if (!IsVisible) Show();
        if (TryCursorDip(out var cursor)) PlaceWindowAtCenter(cursor.X, cursor.Y);
        else PlaceWindow();
        UpdateOrbScreenPos();
        _ignoreDeactivateUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
        OpenCloud();
        ShowToast($"Explorer selection: {files.Length} item(s). Choose a target.", kind: ToastKind.Info);
    }

    private bool TryHandleExplorerBridgeTarget(TargetItem target)
    {
        if (_explorerBridgeFiles is not { Length: > 0 } files) return false;

        if (target.IsGroup)
        {
            EnterGroup(target);
            return true;
        }

        if (!target.Exists)
        {
            ShowMissingMenu(target);
            return true;
        }

        if (DropExplorerFiles(target, files))
        {
            _explorerBridgeFiles = null;
            CloseCloud();
        }
        return true;
    }

    private bool TryAddExplorerBridgeTargets()
    {
        if (_explorerBridgeFiles is not { Length: > 0 } files) return false;

        AddTargets(files.Select(TargetFromPath), _currentGroup);
        RememberDropHistory(
            DropHistoryAction.AddTargets,
            new TargetItem { Name = _currentGroup?.Name ?? "Wheel", Path = "" },
            DropPayloadKind.Files,
            files.Length,
            DropHistoryStatus.Succeeded,
            detail: "Added from Explorer SendTo.");
        _explorerBridgeFiles = null;
        CloseCloud();
        return true;
    }

    private bool DropExplorerFiles(TargetItem target, string[] files)
    {
        var targetKind = DropIntent.ClassifyTarget(
            target,
            LaunchService.IsFolderTarget(target),
            LaunchService.IsRunTarget(target));
        if (TelegramDropService.IsTelegramTarget(target)) targetKind = DropTargetKind.Telegram;

        var compatibility = DropIntent.Compatibility(DropPayloadKind.Files, targetKind);
        if (!compatibility.CanReceive)
        {
            ShowToast(compatibility.Reason, kind: ToastKind.Warning);
            return false;
        }

        if (!ConfirmDropPreflight(target, DropPayloadKind.Files, targetKind, files.Length))
            return false;

        if (targetKind == DropTargetKind.Telegram)
        {
            SendExplorerFilesToTelegram(target, files);
            return true;
        }

        switch (DropDispatch.ClassifyFileDrop(target.IsSorter, LaunchService.IsRunTarget(target)))
        {
            case FileDropRoute.Sort:
                DropSorted(target, files, DropDispatch.ResolveAction(
                    ctrl: false,
                    shift: false,
                    target.Override,
                    TargetStore.Config.GlobalAction));
                return true;
            case FileDropRoute.Run:
                var launched = LaunchService.LaunchWith(target, files);
                RememberDropHistory(
                    DropHistoryAction.Run,
                    target,
                    DropPayloadKind.Files,
                    files.Length,
                    launched ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                    detail: launched ? "Opened from Explorer SendTo." : "LaunchWith returned false.");
                ShowToast(launched
                    ? $"Opened {files.Length} item(s) with {target.Name}"
                    : "Could not launch", kind: launched ? ToastKind.Success : ToastKind.Danger);
                return true;
        }

        var dest = LaunchService.DestPath(target);
        var act = DropDispatch.ResolveAction(
            ctrl: false,
            shift: false,
            target.Override,
            TargetStore.Config.GlobalAction);
        var op = BuildOpBefore(act, files, dest);
        var ok = FileOps.Execute(files, dest, act);
        if (ok) RememberOp(op);
        RememberDropHistory(
            act == DropAction.Move ? DropHistoryAction.Move : DropHistoryAction.Copy,
            target,
            DropPayloadKind.Files,
            files.Length,
            ok ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
            destination: dest,
            detail: "Dropped from Explorer SendTo.");
        ShowToast(ok
            ? $"{(act == DropAction.Move ? "Moved" : "Copied")}: {files.Length} item(s) → {target.Name}"
            : "Operation was not completed", ok, ok ? ToastKind.Success : ToastKind.Danger);
        return true;
    }

    private void SendExplorerFilesToTelegram(TargetItem target, string[] files)
    {
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.FileDrop, files);
        var result = TelegramDropService.CopyToClipboard(
            data,
            System.IO.Path.Combine(TargetStore.Dir, "telegram-drop"));
        if (result == null)
        {
            ShowToast("Nothing to send", kind: ToastKind.Warning);
            return;
        }

        LaunchService.Launch(new TargetItem { Name = target.Name, Path = TelegramDropService.LaunchPathFor(target) });
        TelegramDropService.PasteIntoTelegramWhenReady(pasted =>
        {
            RememberDropHistory(
                DropHistoryAction.Telegram,
                target,
                DropPayloadKind.Files,
                result.Count,
                pasted ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
                detail: pasted
                    ? "Pasted via Explorer SendTo handoff."
                    : "Telegram did not become ready; payload left on clipboard.");
            if (!pasted)
            {
                _ = Dispatcher.InvokeAsync(() => ShowToast(
                    "Telegram did not become ready. The payload is still on the clipboard.",
                    kind: ToastKind.Warning));
            }
        });
        ShowToast($"Copied {result.Count} file(s); waiting for Telegram", kind: ToastKind.Info);
    }
}
