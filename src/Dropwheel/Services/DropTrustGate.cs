using Dropwheel.Models;

namespace Dropwheel.Services;

public readonly record struct DropPreflight(
    string Caption,
    string Message,
    string PrimaryText);

public static class DropTrustGate
{
    public static DropPreflight? Evaluate(
        TargetItem target,
        DropPayloadKind payload,
        DropTargetKind targetKind,
        int itemCount)
    {
        if (targetKind == DropTargetKind.Run && payload == DropPayloadKind.Files)
        {
            return new DropPreflight(
                "Run dropped files?",
                $"Dropwheel will run {target.Name} with {CountText(itemCount, "item")} as input.",
                "Run");
        }

        if (targetKind == DropTargetKind.Telegram
            && payload is DropPayloadKind.Files or DropPayloadKind.VirtualFiles or DropPayloadKind.Link or DropPayloadKind.Text)
        {
            return new DropPreflight(
                "Send through Telegram?",
                "Dropwheel will copy this payload to the clipboard, open Telegram, and paste it when Telegram is active.",
                "Send");
        }

        if (targetKind == DropTargetKind.Sorter
            && target.Watch
            && payload is DropPayloadKind.Files or DropPayloadKind.VirtualFiles or DropPayloadKind.Text)
        {
            return new DropPreflight(
                "Sort with watched rules?",
                $"Dropwheel will route {CountText(itemCount, "item")} by {target.Name}'s rules. This target also moves future files automatically while Watch is enabled.",
                "Sort");
        }

        return null;
    }

    private static string CountText(int count, string unit) =>
        count > 0 ? $"{count} {unit}{(count == 1 ? "" : "s")}" : $"the {unit}s";
}
