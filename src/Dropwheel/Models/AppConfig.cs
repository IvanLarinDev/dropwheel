namespace Dropwheel.Models;

public class AppConfig
{
    public DropAction GlobalAction { get; set; } = DropAction.Copy;

    // Orb center (DIP, virtual screen); NaN = default (right edge of primary).
    // -1 is no good as a marker: a monitor left of the primary yields negative coordinates.
    public double OrbX { get; set; } = double.NaN;
    public double OrbY { get; set; } = double.NaN;

    public double OrbOpacity { get; set; } = 0.8;
    public int HoverDelayMs { get; set; } = 250;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>Seconds of inactivity before the orb dims (0 = off).</summary>
    public int IdleFadeSeconds { get; set; } = 0;

    /// <summary>UI theme: Fluent, Dark, Light, Neon.</summary>
    public string Theme { get; set; } = "Fluent";

    public List<TargetItem> Targets { get; set; } = new();

    /// <summary>File-type presets for the rules editor. Seeded with built-in defaults on first
    /// run, then user-editable. null (missing key) triggers reseeding on load.</summary>
    public List<FilePreset>? Presets { get; set; }

    /// <summary>User-editable launch commands for script-like targets. Missing key triggers
    /// reseeding on load; an empty list means "use only shell execution".</summary>
    public List<LaunchCommand>? LaunchCommands { get; set; }
}

public sealed class LaunchCommand
{
    public List<string> Extensions { get; set; } = new();
    public string FileName { get; set; } = "";
    public string Arguments { get; set; } = "";
}
