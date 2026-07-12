using System.IO;
using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Built-in file-type presets used to seed a fresh config. Once written to
/// config.json the user can edit, add, or remove categories freely.</summary>
public static class PresetService
{
    /// <summary>Builds a preset from a rule for "Save as preset". Presets are extension-based,
    /// so returns null when the rule has no Extension condition. An absolute destination is
    /// stored as its leaf folder name to keep the preset portable.</summary>
    public static FilePreset? FromRule(string name, SortRule rule)
    {
        var ext = rule.All.FirstOrDefault(c => c.Field == ConditionField.Extension)?.Value;
        if (string.IsNullOrWhiteSpace(ext)) return null;
        return new FilePreset { Name = name.Trim(), Dest = PresetDest(rule.Dest), Extensions = ext.Trim() };
    }

    private static string PresetDest(string dest)
    {
        if (string.IsNullOrWhiteSpace(dest)) return "";
        var trimmed = dest.TrimEnd('\\', '/');
        return Path.IsPathRooted(trimmed) ? Path.GetFileName(trimmed) : trimmed;
    }

    public static List<FilePreset> Defaults() => new()
    {
        new() { Name = "Images", Dest = "Images", Extensions = "png jpg jpeg gif webp bmp tiff svg heic" },
        new() { Name = "Documents", Dest = "Documents", Extensions = "pdf doc docx txt rtf odt md" },
        new() { Name = "Spreadsheets", Dest = "Spreadsheets", Extensions = "xls xlsx csv ods" },
        new() { Name = "Presentations", Dest = "Presentations", Extensions = "ppt pptx odp" },
        new() { Name = "Archives", Dest = "Archives", Extensions = "zip rar 7z tar gz" },
        new() { Name = "Video", Dest = "Video", Extensions = "mp4 mkv avi mov wmv webm" },
        new() { Name = "Audio", Dest = "Audio", Extensions = "mp3 wav flac aac ogg m4a" },
        new() { Name = "Installers", Dest = "Installers", Extensions = "exe msi" },
    };
}
