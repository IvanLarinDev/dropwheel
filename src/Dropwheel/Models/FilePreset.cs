namespace Dropwheel.Models;

/// <summary>A reusable file-type category for the rules editor: a display name, the
/// destination subfolder it seeds, and its extensions. Editable in config.json.</summary>
public sealed class FilePreset
{
    public string Name { get; set; } = "";
    public string Dest { get; set; } = "";
    public string Extensions { get; set; } = "";
}
