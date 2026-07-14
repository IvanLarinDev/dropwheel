using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class TargetStore
{
    public static AppConfig Config { get; private set; } = new();
    internal static string? DirOverride { get; set; }

    /// <summary>Raised after the config is written to disk. The folder watcher listens to this to
    /// re-sync its FileSystemWatchers when targets or their Watch flag change.</summary>
    public static event Action? Saved;

    /// <summary>Every routable target, flattening one level of groups (groups themselves are not
    /// targets). Used to find all sorters that opted into folder watching.</summary>
    public static IEnumerable<TargetItem> AllTargets =>
        Config.Targets.SelectMany(t => t.IsGroup ? (IEnumerable<TargetItem>)t.Children! : new[] { t });

    public static string Dir => DirOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dropwheel");
    public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // OrbX/OrbY use NaN as "not set"
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void Load()
    {
        bool shouldBackup = false;
        if (File.Exists(FilePath))
        {
            try
            {
                var configText = File.ReadAllText(FilePath);
                Config = DeserializeConfig(configText, out var sanitizedInvalidEnums) ?? new();
                var needsSave = sanitizedInvalidEnums;
                if (Config.Presets == null) { Config.Presets = PresetService.Defaults(); needsSave = true; }
                var clampedThreshold = WheelLayout.ClampThreshold(Config.OverflowThreshold);
                if (clampedThreshold != Config.OverflowThreshold) { Config.OverflowThreshold = clampedThreshold; needsSave = true; }
                if (InitializeGroupShortcuts()) needsSave = true;
                if (needsSave) Save();
                return;
            }
            catch (JsonException ex) { ErrorLog.Write("Config is corrupted; backing it up and recreating defaults", ex); shouldBackup = true; }
            catch (IOException ex) { ErrorLog.Write("Config is unreadable; backing it up and recreating defaults", ex); shouldBackup = true; }
            catch (UnauthorizedAccessException ex) { ErrorLog.Write("Config is unreadable; backing it up and recreating defaults", ex); shouldBackup = true; }
        }
        if (shouldBackup && !BackupBadConfig(DateTime.Now))
        {
            Config = Defaults();
            ErrorLog.Write("Settings file could not be backed up; using defaults in memory without overwriting it.");
            return;
        }
        Config = Defaults();
        Save();
    }

    internal static string BackupPath(DateTime now)
    {
        var stamp = now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(Dir, $"config.bad.{stamp}.json");
    }

    private static bool BackupBadConfig(DateTime now)
    {
        try
        {
            if (!File.Exists(FilePath)) return true;
            var backup = BackupPath(now);
            for (int i = 2; File.Exists(backup); i++)
                backup = Path.Combine(Dir, $"config.bad.{now:yyyyMMdd_HHmmss}.{i}.json");
            File.Copy(FilePath, backup);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Failed to back up bad config", ex);
            return false;
        }
    }

    /// <summary>Writes via a temp file then renames it: if the process is killed mid-write, the
    /// target config.json stays intact instead of becoming half-empty.</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Config, Opts));
            File.Move(tmp, FilePath, overwrite: true);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Failed to save settings", ex);
            throw new InvalidOperationException("Could not save settings. See error.log for details.", ex);
        }
    }

    public static IEnumerable<TargetItem> Groups => Config.Targets.Where(t => t.IsGroup);

    public static string? NextAvailableGroupCode(IEnumerable<string?>? reserved = null)
    {
        var used = (reserved ?? Groups.Select(group => group.GroupCode))
            .Where(GroupShortcutSequence.IsValidCode)
            .Select(code => code!)
            .ToHashSet(StringComparer.Ordinal);
        for (int code = 1; code <= 99; code++)
        {
            var candidate = code.ToString();
            if (!used.Contains(candidate)) return candidate;
        }
        return used.Contains("0") ? null : "0";
    }

    private static bool InitializeGroupShortcuts()
    {
        if (Config.GroupShortcutsInitialized) return false;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            if (GroupShortcutSequence.IsValidCode(group.GroupCode) && used.Add(group.GroupCode!))
                continue;

            group.GroupCode = NextAvailableGroupCode(used);
            if (group.GroupCode != null) used.Add(group.GroupCode);
        }
        Config.GroupShortcutsInitialized = true;
        return true;
    }

    public static IReadOnlyList<TargetItem> OrderedForDisplay(IList<TargetItem> targets)
    {
        var indexed = targets.Select((target, index) => new { target, index }).ToArray();
        if (!indexed.Any(x => x.target.TilePosition.HasValue))
            return indexed
                .OrderByDescending(x => x.target.Pinned)
                .ThenBy(x => x.index)
                .Select(x => x.target)
                .ToArray();

        return indexed
            .OrderBy(x => x.target.TilePosition ?? int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.target)
            .ToArray();
    }

    public static void RenumberTilePositions(IList<TargetItem> targets)
    {
        for (int i = 0; i < targets.Count; i++)
            targets[i].TilePosition = i;
    }

    public static bool MoveTileBefore(IList<TargetItem> targets, TargetItem source, TargetItem before)
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

    public static bool MoveTileToIndex(IList<TargetItem> targets, TargetItem source, int destinationIndex)
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

    /// <summary>Mark a target pinned and move it first on its level. The flag alone only orders
    /// tiles while no manual order exists, so the move is what keeps the pin visible afterwards.</summary>
    public static void PinToFront(IList<TargetItem> targets, TargetItem source)
    {
        source.Pinned = true;
        MoveTileToIndex(targets, source, 0);
    }

    public static bool MoveTileToEnd(IList<TargetItem> targets, TargetItem source)
    {
        var ordered = OrderedForDisplay(targets).ToList();
        if (ordered.Count > 0 && ReferenceEquals(ordered[^1], source)) return false;
        if (!ordered.Remove(source)) return false;
        ordered.Add(source);
        ApplyTileOrder(targets, ordered);
        return true;
    }

    private static void ApplyTileOrder(IList<TargetItem> targets, IReadOnlyList<TargetItem> ordered)
    {
        targets.Clear();
        foreach (var target in ordered) targets.Add(target);
        RenumberTilePositions(targets);
    }

    /// <summary>Remove a target everywhere (from the root and all groups). Used both for a real delete
    /// and as the first half of a move — so it must NOT touch the item's cached icon.</summary>
    public static void RemoveEverywhere(TargetItem item)
    {
        Config.Targets.Remove(item);
        foreach (var g in Groups) g.Children!.Remove(item);
    }

    /// <summary>Deletes a target for good (a group takes its children with it) and removes any favicon it
    /// had cached under the icons folder, so a deleted link doesn't leave an orphan file behind. Unlike
    /// RemoveEverywhere this is only for a genuine delete, never a move.</summary>
    public static void DeleteTarget(TargetItem item)
    {
        var orphaned = item.Children != null ? item.Children.Prepend(item).ToList() : new List<TargetItem> { item };
        RemoveEverywhere(item);
        foreach (var gone in orphaned) DeleteCachedIcon(gone);
    }

    /// <summary>Deletes a group but keeps its targets by moving them out to the root level first, then
    /// removing the now-empty container. The children (and their icons) are preserved.</summary>
    public static void DissolveGroup(TargetItem group)
    {
        if (group.Children == null) return;
        foreach (var child in group.Children.ToList()) MoveToGroup(child, null); // to root; empties Children
        RemoveEverywhere(group);
        DeleteCachedIcon(group);
    }

    private static void DeleteCachedIcon(TargetItem item)
    {
        var icon = item.IconPath;
        if (string.IsNullOrWhiteSpace(icon)) return;
        // Another surviving target may share the same cached favicon (same link → same hashed file); keep
        // it if so.
        if (AllTargets.Any(t => string.Equals(t.IconPath, icon, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var iconsRoot = Path.GetFullPath(Path.Combine(Dir, "icons")) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(icon);
            // Only ever delete inside our own icons cache — never a user-chosen custom icon elsewhere.
            if (full.StartsWith(iconsRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                File.Delete(full);
        }
        catch (Exception ex) { ErrorLog.Write($"Could not remove cached icon '{icon}'", ex); }
    }

    /// <summary>Move a target into a group (null = root).</summary>
    public static void MoveToGroup(TargetItem item, TargetItem? group)
    {
        var destination = group?.Children ?? Config.Targets;
        if (ReferenceEquals(ContainingList(item), destination)) return;
        RemoveEverywhere(item);
        item.TilePosition = null;
        destination.Add(item);
    }

    public static TargetItem? FindParentGroup(TargetItem item)
        => Groups.FirstOrDefault(g => g.Children!.Contains(item));

    private static IList<TargetItem>? ContainingList(TargetItem item)
    {
        if (Config.Targets.Contains(item)) return Config.Targets;
        return Groups.Select(g => (IList<TargetItem>)g.Children!).FirstOrDefault(children => children.Contains(item));
    }

    private static AppConfig? DeserializeConfig(string json, out bool sanitizedInvalidEnums)
    {
        try
        {
            sanitizedInvalidEnums = false;
            return JsonSerializer.Deserialize<AppConfig>(json, Opts);
        }
        catch (JsonException ex) when (TrySanitizeInvalidEnums(json, out var sanitizedJson))
        {
            ErrorLog.Write("Config contains unknown enum values; falling back only for those fields.", ex);
            sanitizedInvalidEnums = true;
            return JsonSerializer.Deserialize<AppConfig>(sanitizedJson, Opts);
        }
    }

    private static bool TrySanitizeInvalidEnums(string json, out string sanitizedJson)
    {
        sanitizedJson = json;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject rootObject)
            return false;

        var changed = false;
        changed |= RemoveInvalidEnum<DropAction>(rootObject, nameof(AppConfig.GlobalAction));
        changed |= RemoveInvalidEnum<OpenAnimation>(rootObject, nameof(AppConfig.OpenAnimation));
        changed |= RemoveInvalidEnum<OverflowLayout>(rootObject, nameof(AppConfig.OverflowLayout));

        if (rootObject[nameof(AppConfig.Targets)] is JsonArray targets)
            changed |= SanitizeTargets(targets);

        if (!changed)
            return false;

        sanitizedJson = rootObject.ToJsonString();
        return true;
    }

    private static bool SanitizeTargets(JsonArray targets)
    {
        var changed = false;
        foreach (var targetNode in targets)
        {
            if (targetNode is not JsonObject targetObject)
                continue;

            changed |= RemoveInvalidEnum<DropAction>(targetObject, nameof(TargetItem.Override));
            changed |= RemoveInvalidEnum<ConflictPolicy>(targetObject, nameof(TargetItem.ConflictPolicy));

            if (targetObject[nameof(TargetItem.Rules)] is JsonArray rules)
                changed |= SanitizeRules(rules);

            if (targetObject[nameof(TargetItem.Children)] is JsonArray children)
                changed |= SanitizeTargets(children);
        }

        return changed;
    }

    /// <summary>Strips unknown ConditionField/CompareOp tokens from a target's sort rules the same way
    /// Override is handled, so a config written by a newer build (or hand-edited) with a rule enum this
    /// build doesn't recognize degrades that one field to its default instead of throwing — which would
    /// otherwise wipe the entire config down to defaults.</summary>
    private static bool SanitizeRules(JsonArray rules)
    {
        var changed = false;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject ruleObject)
                continue;
            if (ruleObject[nameof(SortRule.All)] is not JsonArray conditions)
                continue;
            foreach (var conditionNode in conditions)
            {
                if (conditionNode is not JsonObject conditionObject)
                    continue;
                changed |= RemoveInvalidEnum<ConditionField>(conditionObject, nameof(RuleCondition.Field));
                changed |= RemoveInvalidEnum<CompareOp>(conditionObject, nameof(RuleCondition.Op));
            }
        }

        return changed;
    }

    private static bool RemoveInvalidEnum<TEnum>(JsonObject obj, string propertyName)
        where TEnum : struct, Enum
    {
        if (!obj.TryGetPropertyValue(propertyName, out var valueNode) || valueNode is null)
            return false;

        if (valueNode is not JsonValue value)
            return false;

        if (value.TryGetValue<string>(out var enumToken))
        {
            if (Enum.TryParse<TEnum>(enumToken, ignoreCase: true, out _))
                return false;

            obj.Remove(propertyName);
            return true;
        }

        if (value.TryGetValue<int>(out var enumValue) && Enum.IsDefined(typeof(TEnum), enumValue))
            return false;

        obj.Remove(propertyName);
        return true;
    }

    /// <summary>A comparison key for duplicate detection: the same folder, app or link yields the same
    /// key regardless of letter case, trailing slash or slash direction. A dropped .lnk is already
    /// resolved to its real path before this runs, so two shortcuts to one app collapse too. Groups and
    /// pathless items have no key (null) and are never treated as duplicates.</summary>
    internal static string? DedupKey(TargetItem item)
    {
        if (item.IsGroup) return null;
        var path = item.Path;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (item.IsUri) return path.Trim().ToLowerInvariant();

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            full = path;
        }
        return full.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }

    /// <summary>Splits a batch of candidates into the ones new for <paramref name="level"/> and the ones
    /// that duplicate a target already there — or an earlier candidate in the same batch. Each duplicate
    /// is paired with the existing target it collided with, so the caller can highlight that tile. Groups
    /// and pathless items are always kept as new. The incoming order is preserved.</summary>
    internal static (List<TargetItem> New, List<TargetItem> Existing) SplitNewAndDuplicates(
        IEnumerable<TargetItem> level, IEnumerable<TargetItem> candidates)
    {
        var byKey = new Dictionary<string, TargetItem>(StringComparer.Ordinal);
        foreach (var item in level)
            if (DedupKey(item) is { } key) byKey.TryAdd(key, item);

        var fresh = new List<TargetItem>();
        var collided = new List<TargetItem>();
        foreach (var candidate in candidates)
        {
            var key = DedupKey(candidate);
            if (key == null) { fresh.Add(candidate); continue; }
            if (byKey.TryGetValue(key, out var existing)) { collided.Add(existing); continue; }
            byKey[key] = candidate;
            fresh.Add(candidate);
        }
        return (fresh, collided);
    }

    private static AppConfig Defaults()
    {
        static string P(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        return new AppConfig
        {
            GroupShortcutsInitialized = true,
            Presets = PresetService.Defaults(),
            Targets = {
                new() { Name = "Downloads", Path = Path.Combine(P(Environment.SpecialFolder.UserProfile), "Downloads"), Pinned = true },
                new() { Name = "Documents", Path = P(Environment.SpecialFolder.MyDocuments), Pinned = true },
                new() { Name = "Desktop",   Path = P(Environment.SpecialFolder.DesktopDirectory) },
                new() { Name = "Pictures",  Path = P(Environment.SpecialFolder.MyPictures) },
            },
        };
    }
}
