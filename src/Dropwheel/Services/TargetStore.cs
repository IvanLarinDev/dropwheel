using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new();
                if (Config.Presets == null) { Config.Presets = PresetService.Defaults(); Save(); }
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

    /// <summary>Remove a target everywhere (from the root and all groups).</summary>
    public static void RemoveEverywhere(TargetItem item)
    {
        Config.Targets.Remove(item);
        foreach (var g in Groups) g.Children!.Remove(item);
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

    private static AppConfig Defaults()
    {
        static string P(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        return new AppConfig
        {
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
