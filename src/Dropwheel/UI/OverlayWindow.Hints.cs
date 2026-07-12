using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private const int GestureHintMax = 3;

    /// <summary>Shows a one-off tip at most <paramref name="max"/> times ever, counting shows in config.
    /// Returns whether it actually showed, so a caller can stop at one hint per occasion.</summary>
    private bool ShowHintOnce(string id, string message, int max)
    {
        if (!HintPolicy.RecordAndAllow(TargetStore.Config.HintShows, id, max)) return false;
        TrySaveConfig();
        ShowToast(message);
        return true;
    }

    /// <summary>On opening the wheel, surface at most one tip about a gesture the user can't find by
    /// looking — the digit shortcut for groups, or middle-click sorting — since the tiles don't reveal
    /// them. Fires after the trigger (an open that shows such a tile), not on hover.</summary>
    private void TryShowOpenHint()
    {
        var level = CurrentLevelTargets();
        if (level.Any(t => t.IsGroup && GroupShortcutSequence.IsValidCode(t.GroupCode))
            && ShowHintOnce("digit-shortcut", "Over the orb, type a group's number to open it", GestureHintMax))
            return;
        if (level.Any(t => t.IsSorter))
            ShowHintOnce("middle-click-sort",
                "Middle-click a sorter to sort the files already in it", GestureHintMax);
    }

    /// <summary>Once ever, after the orb returns from hiding behind a fullscreen app, explains why it
    /// vanished — a balloon can't show during fullscreen (Focus Assist mutes it), so the notice waits
    /// for the orb to come back.</summary>
    private void ShowFullscreenReturnHint() =>
        ShowHintOnce("fullscreen", "The orb hides while a fullscreen app is active", 1);
}
