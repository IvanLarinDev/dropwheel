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
    }
}
