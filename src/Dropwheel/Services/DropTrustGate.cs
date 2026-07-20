using Dropwheel.Models;

namespace Dropwheel.Services;

public readonly record struct DropPreflight(
    string Caption,
    string Message,
    string PrimaryText);

[Flags]
internal enum DropConfirmationKind
{
    None = 0,
    RunDroppedFiles = 1,
    TelegramHandoff = 2,
    WatchedSorterRules = 4,
}

public static class DropTrustGate
{
    private static int _bypassedConfirmations;

    internal static bool IsBypassed(DropConfirmationKind kind) =>
        (CurrentBypasses & kind) != 0;

    internal static void SetBypassed(DropConfirmationKind kind, bool bypassed)
    {
        if (bypassed) Interlocked.Or(ref _bypassedConfirmations, (int)kind);
        else Interlocked.And(ref _bypassedConfirmations, ~(int)kind);
    }

    public static DropPreflight? Evaluate(
        TargetItem target,
        DropPayloadKind payload,
        DropTargetKind targetKind,
        int itemCount) =>
        Evaluate(target, payload, targetKind, itemCount, CurrentBypasses);

    internal static DropPreflight? Evaluate(
        TargetItem target,
        DropPayloadKind payload,
        DropTargetKind targetKind,
        int itemCount,
        DropConfirmationKind bypasses)
    {
        if (targetKind == DropTargetKind.Run && payload == DropPayloadKind.Files)
        {
            if ((bypasses & DropConfirmationKind.RunDroppedFiles) != 0) return null;
            return new DropPreflight(
                "Run dropped files?",
                $"Dropwheel will run {target.Name} with {CountText(itemCount, "item")} as input.",
                "Run");
        }

        if (targetKind == DropTargetKind.Telegram
            && payload is DropPayloadKind.Files or DropPayloadKind.VirtualFiles or DropPayloadKind.Link or DropPayloadKind.Text)
        {
            if ((bypasses & DropConfirmationKind.TelegramHandoff) != 0) return null;
            return new DropPreflight(
                "Send through Telegram?",
                "Dropwheel will copy this payload to the clipboard, open Telegram, and paste it when Telegram is active.",
                "Send");
        }

        if (targetKind == DropTargetKind.Sorter
            && target.Watch
            && payload is DropPayloadKind.Files or DropPayloadKind.VirtualFiles or DropPayloadKind.Text)
        {
            if ((bypasses & DropConfirmationKind.WatchedSorterRules) != 0) return null;
            return new DropPreflight(
                "Sort with watched rules?",
                $"Dropwheel will route {CountText(itemCount, "item")} by {target.Name}'s rules. This target also moves future files automatically while Watch is enabled.",
                "Sort");
        }

        return null;
    }

    private static DropConfirmationKind CurrentBypasses =>
        (DropConfirmationKind)Volatile.Read(ref _bypassedConfirmations);

    private static string CountText(int count, string unit) =>
        count > 0 ? $"{count} {unit}{(count == 1 ? "" : "s")}" : $"the {unit}s";
}
