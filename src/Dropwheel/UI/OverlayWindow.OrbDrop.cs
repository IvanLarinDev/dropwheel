using System.Windows;
using Dropwheel.Models;
using Dropwheel.Services;
using IOPath = System.IO.Path;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Dropping on the orb adds a target. If a group level is open,
    /// the target goes into it; otherwise into the root.</summary>
    private void OnOrbDrop(object sender, DragEventArgs e)
    {
        if (AddTargetsFromDrop(e.Data, _currentGroup)) e.Handled = true;
    }

    /// <summary>A quick drop on a group bubble (before hover-expand fires)
    /// adds the target into the group.</summary>
    private void OnGroupDrop(TargetItem group, DragEventArgs e)
    {
        _groupHover?.Stop();
        if (AddTargetsFromDrop(e.Data, group)) e.Handled = true;
    }

    private void OnAddTargetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAddTarget(e.Data) ? AddTargetDropEffect(e) : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool CanAddTarget(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || LinkTargetService.HasPotentialLaunchUriData(data);

    private static DragDropEffects AddTargetDropEffect(DragEventArgs e)
    {
        if (e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return DragDropEffects.Copy;
        if (e.AllowedEffects.HasFlag(DragDropEffects.Link)) return DragDropEffects.Link;
        return DragDropEffects.None;
    }

    private bool AddTargetsFromDrop(IDataObject data, TargetItem? group)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            AddTargets(paths.Select(TargetFromPath), group);
            return true;
        }

        if (LinkTargetService.CreateTarget(data) is { } linkTarget)
        {
            AddTargets(new[] { linkTarget }, group);
            return true;
        }

        if (LinkTargetService.HasSavedMessagesLabel(data)
            && PromptSavedMessagesTarget() is { } savedMessagesTarget)
        {
            AddTargets(new[] { savedMessagesTarget }, group);
            return true;
        }

        return false;
    }

    private TargetItem? PromptSavedMessagesTarget()
    {
        var prompt = new PromptWindow(
            "Telegram Saved Messages",
            "Enter your Telegram username or phone number:")
        { Owner = this };

        if (prompt.ShowDialog() != true) return null;
        if (LinkTargetService.CreateSavedMessagesTarget(prompt.Value) is { } target) return target;

        ShowToast("Saved Messages target needs a username or phone");
        return null;
    }

    internal static TargetItem TargetFromPath(string path)
    {
        // A dropped .lnk becomes a target for what it points at, not the shortcut file.
        var target = ShortcutResolver.Resolve(path);
        // Keep the shortcut's friendly label (e.g. "Visual Studio Code") over the raw target name.
        var name = IOPath.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = IOPath.GetFileNameWithoutExtension(target);
        if (string.IsNullOrEmpty(name)) name = target;
        return new TargetItem { Name = name, Path = target };
    }

    /// <summary>Adds targets to a level. When <paramref name="pinned"/> is set the items are pinned
    /// back-to-front, so a multi-item batch keeps its original order at the head of the level, and
    /// they fly in from <paramref name="origin"/> (the orb centre by default).</summary>
    private void AddTargets(IEnumerable<TargetItem> targets, TargetItem? group, bool pinned = false, Point? origin = null)
    {
        var items = targets.ToArray();
        if (items.Length == 0) return;

        var list = group?.Children ?? TargetStore.Config.Targets;

        IReadOnlyList<TargetItem> duplicates = Array.Empty<TargetItem>();
        if (TargetStore.Config.DeduplicateTargets)
        {
            var (fresh, existing) = TargetStore.SplitNewAndDuplicates(list, items);
            duplicates = existing;
            if (fresh.Count == 0)
            {
                ShowToast(ToastAllDuplicates(existing.Count, group));
                PulseExistingTiles(existing, group);
                return;
            }
            items = fresh.ToArray();
        }

        RememberAdd(list);
        foreach (var item in items) list.Add(item);
        if (pinned)
            foreach (var item in items.Reverse()) TargetStore.PinToFront(list, item);

        bool saved = TrySaveConfig(); // a full disk mustn't crash the drop or skip the UI refresh
        var addToast = ToastForAdd(items.Length, group, pinned, duplicates.Count);
        ShowToast(saved ? addToast : $"{addToast}, but couldn't save to disk", canUndo: true);
        if (_open)
        {
            // Only the newly added tiles animate in (arc from the hub); tiles already on screen slide to
            // their new slots instead of replaying the whole opening animation. Adding into a group that
            // isn't the current level passes no arrivals, so the view just refreshes (group count) quietly.
            var arrivedHere = ReferenceEquals(group, _currentGroup) ? items : Array.Empty<TargetItem>();
            RebuildWithArrival(arrivedHere, origin ?? new Point(HalfSize, HalfSize));
        }
        if (duplicates.Count > 0) PulseExistingTiles(duplicates, group);
        RefreshLinkMetadata(items);
    }

    private static string ToastForAdd(int count, TargetItem? group, bool pinned, int skipped = 0)
    {
        var text = group != null
            ? (pinned ? $"Pinned in {group.Name}: {count}" : $"Added to {group.Name}: {count}")
            : (pinned ? $"Pinned: {count}" : $"Targets added: {count}");
        return skipped > 0 ? $"{text} · {skipped} already there" : text;
    }

    private static string ToastAllDuplicates(int count, TargetItem? group)
    {
        var where = group != null ? $" in {group.Name}" : "";
        return count > 1 ? $"Already on the wheel{where}: {count}" : $"Already on the wheel{where}";
    }

    private void RefreshLinkMetadata(TargetItem[] items)
    {
        var pending = items
            .Where(item => LinkMetadataService.SourceUri(item) != null)
            .Select(item => new PendingLinkMetadata(item, item.Name))
            .ToArray();
        if (pending.Length == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var updates = new List<(PendingLinkMetadata Pending, LinkMetadataUpdate Update)>();
                foreach (var item in pending)
                {
                    if (await LinkMetadataService.FetchAsync(item.Target) is { } update)
                        updates.Add((item, update));
                }

                if (updates.Count == 0) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    var changed = false;
                    foreach (var (pendingItem, update) in updates)
                    {
                        var target = pendingItem.Target;
                        if (!TargetStore.AllTargets.Any(item => ReferenceEquals(item, target))) continue;

                        if (!string.IsNullOrWhiteSpace(update.Title)
                            && target.Name == pendingItem.OriginalName
                            && target.Name != update.Title)
                        {
                            target.Name = update.Title;
                            changed = true;
                        }

                        if (!string.IsNullOrWhiteSpace(update.IconPath)
                            && target.IconPath != update.IconPath)
                        {
                            target.IconPath = update.IconPath;
                            changed = true;
                        }
                    }

                    if (!changed) return;
                    TargetStore.Save();
                    if (_open) BuildCloud();
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not refresh link metadata", ex);
            }
        });
    }

    private sealed record PendingLinkMetadata(TargetItem Target, string OriginalName);
}
