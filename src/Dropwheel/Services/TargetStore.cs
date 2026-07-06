using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dropwheel.Models;

namespace Dropwheel.Services;

public static class TargetStore
{
    public static AppConfig Config { get; private set; } = new();

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
        try
        {
            if (File.Exists(FilePath))
            {
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new();
                if (Config.Presets == null) { Config.Presets = PresetService.Defaults(); Save(); }
                return;
            }
        }
        catch { /* corrupted config — recreate with defaults */ }
        Config = Defaults();
        Save();
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Config, Opts));
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
