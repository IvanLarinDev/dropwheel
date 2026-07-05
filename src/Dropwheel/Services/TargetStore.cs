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
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new();
                return;
            }
        }
        catch { /* повреждённый конфиг — пересоздаём с дефолтами */ }
        Config = Defaults();
        Save();
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Config, Opts));
    }

    private static AppConfig Defaults()
    {
        static string P(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        return new AppConfig { Targets = {
            new() { Name = "Downloads", Path = Path.Combine(P(Environment.SpecialFolder.UserProfile), "Downloads"), Pinned = true },
            new() { Name = "Documents", Path = P(Environment.SpecialFolder.MyDocuments), Pinned = true },
            new() { Name = "Desktop",   Path = P(Environment.SpecialFolder.DesktopDirectory) },
            new() { Name = "Pictures",  Path = P(Environment.SpecialFolder.MyPictures) },
        }};
    }
}
