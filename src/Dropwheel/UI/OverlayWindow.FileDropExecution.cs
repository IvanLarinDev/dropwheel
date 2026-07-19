using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private void ExecuteRealFileDrop(
        TargetItem target,
        string[] files,
        RealFileDropPlan plan,
        bool fromExplorer)
    {
        switch (plan.Route)
        {
            case RealFileDropRoute.Denied:
                ShowToast(plan.DenialReason ?? "Target cannot receive this drop.", kind: ToastKind.Warning);
                return;
            case RealFileDropRoute.Telegram:
                SendFilesToTelegram(target, files, fromExplorer);
                return;
            case RealFileDropRoute.Sort:
                DropSorted(target, files, plan.Action);
                return;
            case RealFileDropRoute.Run:
                ExecuteRunDrop(target, files, fromExplorer);
                return;
            case RealFileDropRoute.CopyMove:
                ExecuteCopyMoveDrop(target, files, plan.Action, plan.Destination, fromExplorer);
                return;
        }
    }

    private void ExecuteRunDrop(TargetItem target, string[] files, bool fromExplorer)
    {
        var launched = LaunchService.LaunchWith(target, files);
        RememberDropHistory(
            DropHistoryAction.Run,
            target,
            DropPayloadKind.Files,
            files.Length,
            launched ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
            detail: launched
                ? fromExplorer ? "Opened from Explorer SendTo." : null
                : "LaunchWith returned false.");
        ShowToast(launched
            ? $"Opened {files.Length} item(s) with {target.Name}"
            : "Could not launch", kind: launched ? ToastKind.Success : ToastKind.Danger);
    }

    private void ExecuteCopyMoveDrop(
        TargetItem target,
        string[] files,
        DropAction action,
        string destination,
        bool fromExplorer)
    {
        FileOp plannedOp;
        FileOperationResult outcome;
        var historyDestination = destination;
        var skipped = 0;
        if (!string.IsNullOrWhiteSpace(target.NameTemplate))
        {
            var now = DateTime.Now;
            var pairs = files
                .Select(file => (Source: file, Dest: Path.Combine(
                    destination,
                    RenamedFileName(target.NameTemplate!, file, now))))
                .ToList();
            if (target.ConflictPolicy == ConflictPolicy.Skip)
            {
                var before = pairs.Count;
                pairs = pairs.Where(pair => !File.Exists(pair.Dest) && !Directory.Exists(pair.Dest)).ToList();
                skipped = before - pairs.Count;
            }
            var sources = pairs.Select(pair => pair.Source).ToArray();
            var destinationPaths = pairs.Select(pair => pair.Dest).ToArray();
            plannedOp = BuildRenamedOp(action, sources, destination, destinationPaths);
            outcome = FileOps.ExecuteToDetailed(pairs, action, policy: target.ConflictPolicy);
            if (destinationPaths.Length == 1) historyDestination = destinationPaths[0];
        }
        else
        {
            var candidates = files;
            if (target.ConflictPolicy == ConflictPolicy.Skip)
            {
                var conflictNames = FileOps.DestinationConflicts(files, destination)
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                candidates = files.Where(file => !conflictNames.Contains(Path.GetFileName(file))).ToArray();
                skipped = files.Length - candidates.Length;
            }
            plannedOp = BuildOpBefore(action, candidates, destination);
            outcome = FileOps.ExecuteDetailed(candidates, destination, action, policy: target.ConflictPolicy);
        }

        RememberFileOutcome(plannedOp, outcome);
        var historyStatus = HistoryStatus(outcome.Status);
        var detailParts = new List<string>();
        if (fromExplorer) detailParts.Add("Dropped from Explorer SendTo.");
        if (outcome.Status == FileOperationStatus.PartiallySucceeded)
            detailParts.Add($"Completed {outcome.CompletedCount} of {outcome.RequestedCount} requested operations.");
        RememberDropHistory(
            action == DropAction.Move ? DropHistoryAction.Move : DropHistoryAction.Copy,
            target,
            DropPayloadKind.Files,
            outcome.CompletedCount,
            historyStatus,
            destination: historyDestination,
            detail: detailParts.Count == 0 ? null : string.Join(" ", detailParts));

        var verb = action == DropAction.Move ? "Moved" : "Copied";
        if (outcome.Status == FileOperationStatus.Succeeded)
        {
            var completed = files.Length - skipped;
            ShowToast(skipped == 0
                ? $"{verb}: {files.Length} item(s) → {target.Name}"
                : completed == 0 ? $"All {skipped} already at {target.Name} — skipped"
                : $"{verb}: {completed} → {target.Name}, {skipped} skipped", kind: ToastKind.Success);
        }
        else if (outcome.Status == FileOperationStatus.PartiallySucceeded)
        {
            ShowToast(
                $"{verb} partially: {outcome.CompletedCount} of {outcome.RequestedCount} item(s)",
                canUndo: outcome.UndoableChanges.Count > 0,
                kind: ToastKind.Warning);
        }
        else
        {
            ShowToast(
                outcome.Status == FileOperationStatus.Cancelled ? "Operation was cancelled" : "Operation was not completed",
                kind: outcome.Status == FileOperationStatus.Cancelled ? ToastKind.Warning : ToastKind.Danger);
        }
    }

    private void SendFilesToTelegram(TargetItem target, string[] files, bool fromExplorer)
    {
        var result = TelegramDropService.CopyFilesToClipboard(files);
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
                    ? fromExplorer ? "Pasted via Explorer SendTo handoff." : "Pasted via clipboard handoff."
                    : "Telegram focus changed or did not become ready; payload left on clipboard.");
            if (!pasted)
            {
                _ = Dispatcher.InvokeAsync(() => ShowToast(
                    "Telegram focus changed or did not become ready. The payload is still on the clipboard.",
                    kind: ToastKind.Warning));
            }
        });
        ShowToast($"Copied {result.Count} file(s); waiting for Telegram", kind: ToastKind.Info);
    }

    private void RememberFileOutcome(FileOp plannedOp, FileOperationResult outcome)
    {
        if (outcome.Status == FileOperationStatus.Succeeded) RememberOp(plannedOp);
        else if (outcome.UndoableChanges.Count > 0) RememberOp(BuildPartialOp(plannedOp.Act, outcome.UndoableChanges));
    }

    private static DropHistoryStatus HistoryStatus(FileOperationStatus status) => status switch
    {
        FileOperationStatus.Succeeded => DropHistoryStatus.Succeeded,
        FileOperationStatus.PartiallySucceeded => DropHistoryStatus.PartiallySucceeded,
        FileOperationStatus.Cancelled => DropHistoryStatus.Cancelled,
        _ => DropHistoryStatus.Failed,
    };
}
