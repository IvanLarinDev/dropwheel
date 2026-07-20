using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Pure collection and ordering operations for the target tree. Persistence and cached-icon
/// lifecycle stay in TargetStore; this type owns no global state and is directly testable.</summary>
internal static class TargetCollection
{
    internal static IEnumerable<TargetItem> Groups(AppConfig config) => config.Targets.Where(item => item.IsGroup);

    internal static string? NextAvailableGroupCode(AppConfig config, IEnumerable<string?>? reserved = null)
    {
        var used = (reserved ?? Groups(config).Select(group => group.GroupCode))
            .Where(GroupShortcutSequence.IsValidCode)
            .Select(code => code!)
            .ToHashSet(StringComparer.Ordinal);
        for (var code = 1; code <= 99; code++)
        {
            var candidate = code.ToString();
            if (!used.Contains(candidate)) return candidate;
        }
        return used.Contains("0") ? null : "0";
    }

    internal static bool InitializeGroupShortcuts(AppConfig config)
    {
        if (config.GroupShortcutsInitialized) return false;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups(config))
        {
            if (GroupShortcutSequence.IsValidCode(group.GroupCode) && used.Add(group.GroupCode!)) continue;
            group.GroupCode = NextAvailableGroupCode(config, used);
            if (group.GroupCode != null) used.Add(group.GroupCode);
        }
        config.GroupShortcutsInitialized = true;
        return true;
    }

    internal static IReadOnlyList<TargetItem> OrderedForDisplay(IList<TargetItem> targets)
    {
        var indexed = targets.Select((target, index) => new { target, index }).ToArray();
        if (!indexed.Any(entry => entry.target.TilePosition.HasValue))
            return indexed.OrderByDescending(entry => entry.target.Pinned)
                .ThenBy(entry => entry.index).Select(entry => entry.target).ToArray();
        return indexed.OrderBy(entry => entry.target.TilePosition ?? int.MaxValue)
            .ThenBy(entry => entry.index).Select(entry => entry.target).ToArray();
    }

    internal static void RenumberTilePositions(IList<TargetItem> targets)
    {
        for (var i = 0; i < targets.Count; i++) targets[i].TilePosition = i;
    }

    internal static bool MoveTileBefore(IList<TargetItem> targets, TargetItem source, TargetItem before)
    {
        if (ReferenceEquals(source, before)) return false;
        var ordered = OrderedForDisplay(targets).ToList();
        if (!ordered.Remove(source)) return false;
        var insert = ordered.IndexOf(before);
        if (insert < 0) return false;
        ordered.Insert(insert, source);
        ApplyTileOrder(targets, ordered);
        return true;
    }

    internal static bool MoveTileToIndex(IList<TargetItem> targets, TargetItem source, int destinationIndex)
    {
        var ordered = OrderedForDisplay(targets).ToList();
        var sourceIndex = ordered.IndexOf(source);
        if (sourceIndex < 0 || ordered.Count == 0) return false;
        destinationIndex = Math.Clamp(destinationIndex, 0, ordered.Count - 1);
        if (sourceIndex == destinationIndex) return false;
        ordered.RemoveAt(sourceIndex);
        ordered.Insert(destinationIndex, source);
        ApplyTileOrder(targets, ordered);
        return true;
    }

    internal static bool MoveTileToEnd(IList<TargetItem> targets, TargetItem source)
    {
        var ordered = OrderedForDisplay(targets).ToList();
        if (ordered.Count > 0 && ReferenceEquals(ordered[^1], source)) return false;
        if (!ordered.Remove(source)) return false;
        ordered.Add(source);
        ApplyTileOrder(targets, ordered);
        return true;
    }

    internal static void RemoveEverywhere(AppConfig config, TargetItem item)
    {
        config.Targets.Remove(item);
        foreach (var group in Groups(config)) group.Children!.Remove(item);
    }

    internal static void MoveToGroup(AppConfig config, TargetItem item, TargetItem? group)
    {
        var destination = group?.Children ?? config.Targets;
        if (ReferenceEquals(ContainingList(config, item), destination)) return;
        RemoveEverywhere(config, item);
        item.TilePosition = null;
        destination.Add(item);
    }

    internal static TargetItem? FindParentGroup(AppConfig config, TargetItem item) =>
        Groups(config).FirstOrDefault(group => group.Children!.Contains(item));

    private static IList<TargetItem>? ContainingList(AppConfig config, TargetItem item)
    {
        if (config.Targets.Contains(item)) return config.Targets;
        return Groups(config).Select(group => (IList<TargetItem>)group.Children!)
            .FirstOrDefault(children => children.Contains(item));
    }

    private static void ApplyTileOrder(IList<TargetItem> targets, IReadOnlyList<TargetItem> ordered)
    {
        targets.Clear();
        foreach (var target in ordered) targets.Add(target);
        RenumberTilePositions(targets);
    }
}
