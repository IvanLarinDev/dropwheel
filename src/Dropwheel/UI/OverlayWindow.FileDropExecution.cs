using System.IO;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private async Task ExecuteRealFileDropAsync(
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
                await SendFilesToTelegramAsync(target, files, fromExplorer);
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
            pairs = ApplyBatchDestinationPolicy(pairs, target.ConflictPolicy, out skipped);
            var sources = pairs.Select(pair => pair.Source).ToArray();
            var destinationPaths = pairs.Select(pair => pair.Dest).ToArray();
            plannedOp = BuildRenamedOp(action, sources, destination, destinationPaths);
            outcome = FileOps.ExecuteToDetailed(pairs, action, policy: target.ConflictPolicy);
            if (destinationPaths.Length == 1) historyDestination = destinationPaths[0];
        }
        else
        {
            var pairs = files.Select(file => (
                Source: file,
                Dest: Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(file)))));
            var candidates = ApplyBatchDestinationPolicy(pairs, target.ConflictPolicy, out skipped)
                .Select(pair => pair.Source)
                .ToArray();
            plannedOp = BuildOpBefore(action, candidates, destination);
            outcome = FileOps.ExecuteDetailed(candidates, destination, action, policy: target.ConflictPolicy);
        }

        var canUndo = RememberFileOutcome(plannedOp, outcome);
        var historyStatus = HistoryStatus(outcome.Status);
        var detailParts = new List<string>();
        if (fromExplorer) detailParts.Add("Dropped from Explorer SendTo.");
        if (skipped > 0) detailParts.Add($"Skipped {skipped} colliding destination(s).");
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
                : $"{verb}: {completed} → {target.Name}, {skipped} skipped",
                canUndo: canUndo,
                kind: ToastKind.Success);
        }
        else if (outcome.Status == FileOperationStatus.PartiallySucceeded)
        {
            ShowToast(
                $"{verb} partially: {outcome.CompletedCount} of {outcome.RequestedCount} item(s)",
                canUndo: canUndo,
                kind: ToastKind.Warning);
        }
        else
        {
            ShowToast(
                outcome.Status == FileOperationStatus.Cancelled ? "Operation was cancelled" : "Operation was not completed",
                kind: outcome.Status == FileOperationStatus.Cancelled ? ToastKind.Warning : ToastKind.Danger);
        }
    }

    private async Task SendFilesToTelegramAsync(TargetItem target, string[] files, bool fromExplorer)
    {
        var result = TelegramDropService.CopyFilesToClipboard(files);
        if (result == null)
        {
            ShowToast("Nothing to send", kind: ToastKind.Warning);
            return;
        }

        LaunchService.Launch(new TargetItem { Name = target.Name, Path = TelegramDropService.LaunchPathFor(target) });
        ShowToast($"Copied {result.Count} file(s); waiting for Telegram", kind: ToastKind.Info);
        await CompleteTelegramPasteAsync(
            target,
            DropPayloadKind.Files,
            result.Count,
            fromExplorer ? "Pasted via Explorer SendTo handoff." : "Pasted via clipboard handoff.");
    }

    private async Task CompleteTelegramPasteAsync(
        TargetItem target,
        DropPayloadKind payload,
        int count,
        string successDetail)
    {
        var pasted = await TelegramDropService.PasteIntoTelegramWhenReadyAsync();
        RememberDropHistory(
            DropHistoryAction.Telegram,
            target,
            payload,
            count,
            pasted ? DropHistoryStatus.Succeeded : DropHistoryStatus.Failed,
            detail: pasted
                ? successDetail
                : "Telegram focus changed or did not become ready; payload left on clipboard.");
        if (!pasted)
        {
            ShowToast(
                "Telegram focus changed or did not become ready. The payload is still on the clipboard.",
                kind: ToastKind.Warning);
        }
    }

    private bool RememberFileOutcome(FileOp plannedOp, FileOperationResult outcome)
    {
        var actual = outcome.UndoableChanges.Count > 0
            ? BuildPartialOp(plannedOp.Act, outcome.UndoableChanges)
            : default(FileOp?);
        if (actual is { } op && CanUndo(op))
        {
            RememberOp(op);
            return true;
        }
        return false;
    }

    /// <summary>Within one batch, Ask/Overwrite/Skip may target a path at most once. Allowing two
    /// sources to overwrite the same computed name loses the first result and makes one-to-one Undo
    /// impossible. KeepBoth is the only policy whose explicit contract makes every duplicate unique.</summary>
    internal static List<(string Source, string Dest)> ApplyBatchDestinationPolicy(
        IEnumerable<(string Source, string Dest)> pairs,
        ConflictPolicy policy,
        out int skipped)
    {
        var result = new List<(string Source, string Dest)>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        skipped = 0;
        foreach (var pair in pairs)
        {
            var destination = Path.GetFullPath(pair.Dest);
            var duplicate = policy != ConflictPolicy.KeepBoth && !destinations.Add(destination);
            if (policy == ConflictPolicy.KeepBoth) destinations.Add(destination);
            var existing = policy == ConflictPolicy.Skip
                && (File.Exists(destination) || Directory.Exists(destination));
            if (duplicate || existing) { skipped++; continue; }
            result.Add((pair.Source, destination));
        }
        return result;
    }

    private static DropHistoryStatus HistoryStatus(FileOperationStatus status) => status switch
    {
        FileOperationStatus.Succeeded => DropHistoryStatus.Succeeded,
        FileOperationStatus.PartiallySucceeded => DropHistoryStatus.PartiallySucceeded,
        FileOperationStatus.Cancelled => DropHistoryStatus.Cancelled,
        _ => DropHistoryStatus.Failed,
    };
}
