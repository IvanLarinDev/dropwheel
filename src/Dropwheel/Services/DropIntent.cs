using Dropwheel.Models;

namespace Dropwheel.Services;

public enum DropPayloadKind { Unsupported, Files, VirtualFiles, Link, Text }

public enum DropTargetKind { Missing, Group, Folder, Sorter, Run, Telegram, Unsupported }

public enum DropCompatibilityLevel { Blocked, Allowed, Caution }

public readonly record struct DropCompatibility(
    bool CanReceive,
    DropCompatibilityLevel Level,
    string Reason)
{
    public static DropCompatibility Allow(DropCompatibilityLevel level, string reason) =>
        new(true, level, reason);

    public static DropCompatibility Deny(string reason) =>
        new(false, DropCompatibilityLevel.Blocked, reason);
}

public static class DropIntent
{
    public static DropTargetKind ClassifyTarget(TargetItem target, bool isFolderTarget, bool isRunTarget)
    {
        if (target.IsGroup) return DropTargetKind.Group;
        if (TelegramDropService.IsTelegramTarget(target)) return DropTargetKind.Telegram;
        if (isRunTarget) return DropTargetKind.Run;
        if (isFolderTarget) return target.IsSorter ? DropTargetKind.Sorter : DropTargetKind.Folder;
        return target.Exists ? DropTargetKind.Unsupported : DropTargetKind.Missing;
    }

    public static DropCompatibility Compatibility(DropPayloadKind payload, DropTargetKind target) =>
        payload switch
        {
            DropPayloadKind.Unsupported => DropCompatibility.Deny("This payload is not supported."),
            DropPayloadKind.Files => FileCompatibility(target),
            DropPayloadKind.VirtualFiles => VirtualFileCompatibility(target),
            DropPayloadKind.Link => LinkCompatibility(target),
            DropPayloadKind.Text => TextCompatibility(target),
            _ => DropCompatibility.Deny("This payload is not supported."),
        };

    private static DropCompatibility FileCompatibility(DropTargetKind target) =>
        target switch
        {
            DropTargetKind.Folder => DropCompatibility.Allow(
                DropCompatibilityLevel.Allowed,
                "Can copy or move files."),
            DropTargetKind.Sorter => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can route files by rules."),
            DropTargetKind.Run => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can open dropped files with this target."),
            DropTargetKind.Telegram => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can copy files for Telegram handoff."),
            _ => BlockedTarget(target),
        };

    private static DropCompatibility VirtualFileCompatibility(DropTargetKind target) =>
        target switch
        {
            DropTargetKind.Folder => DropCompatibility.Allow(
                DropCompatibilityLevel.Allowed,
                "Can save virtual files."),
            DropTargetKind.Sorter => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can save and route virtual files."),
            DropTargetKind.Telegram => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can save virtual files for Telegram handoff."),
            DropTargetKind.Run => DropCompatibility.Deny("Run targets need real files."),
            _ => BlockedTarget(target),
        };

    private static DropCompatibility LinkCompatibility(DropTargetKind target) =>
        target switch
        {
            DropTargetKind.Folder or DropTargetKind.Sorter => DropCompatibility.Allow(
                DropCompatibilityLevel.Allowed,
                "Can add the link to the current wheel level."),
            DropTargetKind.Telegram => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can copy link text for Telegram handoff."),
            DropTargetKind.Run => DropCompatibility.Deny("Run targets need real files."),
            _ => BlockedTarget(target),
        };

    private static DropCompatibility TextCompatibility(DropTargetKind target) =>
        target switch
        {
            DropTargetKind.Folder => DropCompatibility.Allow(
                DropCompatibilityLevel.Allowed,
                "Can save text as a file."),
            DropTargetKind.Sorter => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can save and route text."),
            DropTargetKind.Telegram => DropCompatibility.Allow(
                DropCompatibilityLevel.Caution,
                "Can copy text for Telegram handoff."),
            DropTargetKind.Run => DropCompatibility.Deny("Run targets need real files."),
            _ => BlockedTarget(target),
        };

    private static DropCompatibility BlockedTarget(DropTargetKind target) =>
        target switch
        {
            DropTargetKind.Missing => DropCompatibility.Deny("Target is missing."),
            DropTargetKind.Group => DropCompatibility.Deny("Open the group before dropping."),
            _ => DropCompatibility.Deny("Target cannot receive this drop."),
        };
}
