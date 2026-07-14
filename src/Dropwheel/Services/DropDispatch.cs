using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>How a real file drop onto a target is routed. Sort wins over Run, which wins over a plain
/// copy/move — the same order the drop handler applies. Telegram is a separate target-type gate handled
/// before this and is not part of this enum.</summary>
public enum FileDropRoute { Sort, Run, CopyMove }

/// <summary>The pure decision logic behind drop dispatch, kept side-effect free so the precedence rules
/// can be unit-tested directly instead of only through WPF DragEventArgs. The UI layer supplies the
/// already-computed payload/target/modifier flags and acts on the result.</summary>
public static class DropDispatch
{
    /// <summary>Resolves copy-vs-move for a file drop by fixed precedence: Ctrl forces copy, Shift forces
    /// move, then the target's own override, then the global default.</summary>
    public static DropAction ResolveAction(bool ctrl, bool shift, DropAction targetOverride, DropAction globalAction)
    {
        if (ctrl) return DropAction.Copy;
        if (shift) return DropAction.Move;
        if (targetOverride != DropAction.Inherit) return targetOverride;
        return globalAction;
    }

    /// <summary>The action actually applied to a drop: virtual files and dropped text are always saved
    /// (copied) regardless of modifiers; everything else follows <see cref="ResolveAction"/>.</summary>
    public static DropAction EffectiveAction(bool copyOnly, bool ctrl, bool shift, DropAction targetOverride, DropAction globalAction)
        => copyOnly ? DropAction.Copy : ResolveAction(ctrl, shift, targetOverride, globalAction);

    /// <summary>Routes a real file drop onto a target: a sorter routes by its rules, an executable/script
    /// runs with the files, anything else is a plain copy/move into the folder.</summary>
    public static FileDropRoute ClassifyFileDrop(bool isSorter, bool isRunTarget)
        => isSorter ? FileDropRoute.Sort
         : isRunTarget ? FileDropRoute.Run
         : FileDropRoute.CopyMove;

    /// <summary>Runtime-only switch, toggled from the tray "Pause sorting" item. While on, both the
    /// background folder watcher and a manual drop on a sorter skip the rules — files just land in the
    /// folder — so a target can be filled without being distributed. Resets on restart.</summary>
    public static bool SortingPaused { get; set; }

    /// <summary>Whether a sorter tile routes a drop through its rules right now. Every manual drop
    /// path (files, virtual files, text, Explorer SendTo) must gate on this rather than on IsSorter
    /// alone, so "Pause sorting" turns the sorter into a plain folder for all payloads alike.</summary>
    public static bool SortsNow(bool isSorter) => isSorter && !SortingPaused;
}
