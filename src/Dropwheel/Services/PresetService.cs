using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Built-in file-type presets used to seed a fresh config. Once written to
/// config.json the user can edit, add, or remove categories freely.</summary>
public static class PresetService
{
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
