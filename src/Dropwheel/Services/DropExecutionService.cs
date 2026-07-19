using Dropwheel.Models;

namespace Dropwheel.Services;

internal enum RealFileDropRoute { Denied, Telegram, Sort, Run, CopyMove }

internal readonly record struct RealFileDropPlan(
    RealFileDropRoute Route,
    DropAction Action,
    DropTargetKind TargetKind,
    string Destination,
    string? DenialReason = null);

/// <summary>One UI-independent routing contract for real files, shared by WPF drag/drop and Explorer
/// SendTo. It decides what should happen; the overlay owns presentation and records the result.</summary>
internal static class DropExecutionService
{
    public static RealFileDropPlan PlanRealFiles(
        TargetItem target,
        bool ctrl,
        bool shift,
        DropAction globalAction,
        bool sortingPaused)
    {
        var isFolder = LaunchService.IsFolderTarget(target);
        var isRun = LaunchService.IsRunTarget(target);
        var targetKind = TelegramDropService.IsTelegramTarget(target)
            ? DropTargetKind.Telegram
            : DropIntent.ClassifyTarget(target, isFolder, isRun);
        var compatibility = DropIntent.Compatibility(DropPayloadKind.Files, targetKind);
        var action = DropDispatch.ResolveAction(ctrl, shift, target.Override, globalAction);
        var destination = LaunchService.DestPath(target);

        if (!compatibility.CanReceive)
            return new RealFileDropPlan(RealFileDropRoute.Denied, action, targetKind, destination, compatibility.Reason);
        if (targetKind == DropTargetKind.Telegram)
            return new RealFileDropPlan(RealFileDropRoute.Telegram, DropAction.Copy, targetKind, destination);
        if (target.IsSorter && !sortingPaused)
            return new RealFileDropPlan(RealFileDropRoute.Sort, action, targetKind, destination);
        if (isRun)
            return new RealFileDropPlan(RealFileDropRoute.Run, action, targetKind, destination);
        return new RealFileDropPlan(RealFileDropRoute.CopyMove, action, targetKind, destination);
    }
}
