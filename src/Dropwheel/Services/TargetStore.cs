using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class TargetStore
{
    public static AppConfig Config { get; private set; } = new();

    /// <summary>Raised after the config is written to disk. The folder watcher listens to this to
    /// re-sync its FileSystemWatchers when targets or their Watch flag change.</summary>
    public static event Action? Saved;

    /// <summary>Every routable target, flattening one level of groups (groups themselves are not
    /// targets). Used to find all sorters that opted into folder watching.</summary>
    public static IEnumerable<TargetItem> AllTargets =>
        Config.Targets.SelectMany(t => t.IsGroup ? (IEnumerable<TargetItem>)t.Children! : new[] { t });

    public static string Dir => Path.Combine(
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
        if (File.Exists(FilePath))
        {
            try
            {
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new();
                if (Config.Presets == null) { Config.Presets = PresetService.Defaults(); Save(); }
                return;
            }
            catch (JsonException) { /* corrupted config — recreate with defaults */ }
            catch (IOException) { /* unreadable config — recreate with defaults */ }
            catch (UnauthorizedAccessException) { /* unreadable config — recreate with defaults */ }
        }
        Config = Defaults();
        Save();
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

    /// <summary>Remove a target everywhere (from the root and all groups).</summary>
    public static void RemoveEverywhere(TargetItem item)
    {
        Config.Targets.Remove(item);
        foreach (var g in Groups) g.Children!.Remove(item);
    }

    /// <summary>Move a target into a group (null = root).</summary>
    public static void MoveToGroup(TargetItem item, TargetItem? group)
    {
        RemoveEverywhere(item);
        (group?.Children ?? Config.Targets).Add(item);
    }

    public static TargetItem? FindParentGroup(TargetItem item)
        => Groups.FirstOrDefault(g => g.Children!.Contains(item));

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
