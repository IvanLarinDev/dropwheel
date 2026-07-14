using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private static void RememberDropHistory(
        DropHistoryAction action,
        TargetItem target,
        DropPayloadKind payload,
        int itemCount,
        DropHistoryStatus status,
        string? destination = null,
        string? detail = null)
    {
        DropHistoryService.Append(new DropHistoryEntry
        {
            AtUtc = DateTimeOffset.UtcNow,
            Action = action,
            Payload = payload,
            Status = status,
            TargetName = target.Name,
            TargetPath = target.Path,
            Destination = destination,
            ItemCount = Math.Max(0, itemCount),
            Detail = detail,
        });

        if (status == DropHistoryStatus.Succeeded
            && TargetStore.Config.CopyDestinationToClipboard
            && !string.IsNullOrEmpty(destination))
        {
            CopyDestinationToClipboard(destination);
        }
    }

    /// <summary>Copies the drop's destination path to the clipboard, swallowing the occasional failure
    /// (another app holding the clipboard) so it never derails the drop.</summary>
    private static void CopyDestinationToClipboard(string destination)
    {
        try { System.Windows.Clipboard.SetText(destination); }
        catch (Exception ex) { ErrorLog.Write("Could not copy the drop destination to the clipboard", ex); }
    }
}
