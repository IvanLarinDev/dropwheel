using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dropwheel.Services;

public enum DropHistoryAction { Copy, Move, Sort, Run, Telegram, SaveVirtualFiles, SaveText, AddTargets }

public enum DropHistoryStatus { Succeeded, Failed, Cancelled }

public sealed class DropHistoryEntry
{
    public DateTimeOffset AtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DropHistoryAction Action { get; init; }
    public DropPayloadKind Payload { get; init; }
    public DropHistoryStatus Status { get; init; }
    public string TargetName { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public string? Destination { get; init; }
    public int ItemCount { get; init; }
    public string? Detail { get; init; }
}

public static class DropHistoryService
{
    private const int DefaultLimit = 50;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(TargetStore.Dir, "drop-history.json");

    public static IReadOnlyList<DropHistoryEntry> Load() => Load(FilePath);

    internal static IReadOnlyList<DropHistoryEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Array.Empty<DropHistoryEntry>();
            var entries = JsonSerializer.Deserialize<List<DropHistoryEntry>>(File.ReadAllText(path), Opts);
            if (entries != null) return entries;
            return Array.Empty<DropHistoryEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<DropHistoryEntry>();
        }
    }

    public static void Append(DropHistoryEntry entry) => Append(entry, FilePath, DefaultLimit);

    internal static void Append(DropHistoryEntry entry, string path, int limit)
    {
        if (limit <= 0) return;

        try
        {
            lock (Gate)
            {
                var entries = Load(path)
                    .Prepend(entry)
                    .Take(limit)
                    .ToArray();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(entries, Opts));
                File.Move(tmp, path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Write("Could not write drop history", ex);
        }
    }
}
