using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dropwheel.Services;

public enum DropHistoryAction { Copy, Move, Sort, Run, Telegram, SaveVirtualFiles, SaveText, AddTargets }

public enum DropHistoryStatus { Succeeded, PartiallySucceeded, Failed, Cancelled }

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

public readonly record struct DropHistoryLocation(string Path, bool SelectFile);

public static class DropHistoryService
{
    private const int DefaultLimit = 50;
    private const int MenuSummaryLimit = 8;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath => Path.Combine(TargetStore.Dir, "drop-history.json");

    public static IReadOnlyList<DropHistoryEntry> Load() => Load(FilePath);

    public static IReadOnlyList<DropHistoryEntry> LoadForMenu() =>
        Load().Take(MenuSummaryLimit).ToArray();

    public static string MenuSummary(DropHistoryEntry entry)
    {
        var time = entry.AtUtc.ToLocalTime().ToString("HH:mm");
        var action = ActionLabel(entry.Action);
        var items = ItemLabel(entry.Action, entry.Payload, entry.ItemCount);
        var target = string.IsNullOrWhiteSpace(entry.TargetName) ? "Unknown" : entry.TargetName;
        var status = entry.Status switch
        {
            DropHistoryStatus.Succeeded => "",
            DropHistoryStatus.PartiallySucceeded => " (Partial)",
            _ => $" ({entry.Status})",
        };
        return $"{time}  {action} {items} -> {target}{status}";
    }

    /// <summary>The menu entries as plain text for the clipboard — one line per drop, the menu summary
    /// plus the destination path when one was recorded. A quick export without opening the json file.</summary>
    public static string ClipboardText(IEnumerable<DropHistoryEntry> entries) =>
        string.Join(Environment.NewLine, entries.Select(e =>
            string.IsNullOrWhiteSpace(e.Destination) ? MenuSummary(e) : $"{MenuSummary(e)} — {e.Destination}"));

    public static DropHistoryLocation? DestinationLocation(DropHistoryEntry entry) =>
        ExistingLocationFor(entry.Destination) ?? ExistingLocationFor(entry.TargetPath);

    public static string? DestinationFolder(DropHistoryEntry entry)
    {
        if (DestinationLocation(entry) is not { } location) return null;
        return location.SelectFile ? Path.GetDirectoryName(location.Path) : location.Path;
    }

    public static string MenuToolTip(DropHistoryEntry entry)
    {
        var lines = new List<string>();
        if (DestinationLocation(entry) is { } location)
            lines.Add($"{(location.SelectFile ? "Reveal" : "Open")}: {location.Path}");
        AddLine(lines, "Destination", entry.Destination);
        AddLine(lines, "Target", entry.TargetPath);
        AddLine(lines, "Detail", entry.Detail);
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "No folder destination";
    }

    public static void EnsureFileExists()
    {
        lock (Gate)
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(TargetStore.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Array.Empty<DropHistoryEntry>(), Opts));
        }
    }

    public static void Clear() => Clear(FilePath);

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

    internal static void Clear(string path)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(Array.Empty<DropHistoryEntry>(), Opts));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ErrorLog.Write("Could not clear drop history", ex);
        }
    }

    private static string ActionLabel(DropHistoryAction action) => action switch
    {
        DropHistoryAction.Copy => "Copied",
        DropHistoryAction.Move => "Moved",
        DropHistoryAction.Sort => "Sorted",
        DropHistoryAction.Run => "Opened",
        DropHistoryAction.Telegram => "Telegram",
        DropHistoryAction.SaveVirtualFiles => "Saved",
        DropHistoryAction.SaveText => "Saved",
        DropHistoryAction.AddTargets => "Added",
        _ => action.ToString(),
    };

    private static string ItemLabel(DropHistoryAction action, DropPayloadKind payload, int count)
    {
        var safeCount = Math.Max(0, count);
        var noun = action == DropHistoryAction.AddTargets
            ? "target"
            : payload switch
            {
                DropPayloadKind.Files => "file",
                DropPayloadKind.VirtualFiles => "virtual file",
                DropPayloadKind.Link => "link",
                DropPayloadKind.Text => "text",
                _ => "item",
            };
        return safeCount == 1 ? $"1 {noun}" : $"{safeCount} {noun}s";
    }

    private static DropHistoryLocation? ExistingLocationFor(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            return null;

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (Directory.Exists(fullPath)) return new DropHistoryLocation(fullPath, SelectFile: false);
        if (File.Exists(fullPath)) return new DropHistoryLocation(fullPath, SelectFile: true);
        return null;
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var line = $"{label}: {value}";
        if (!lines.Contains(line, StringComparer.Ordinal)) lines.Add(line);
    }
}
