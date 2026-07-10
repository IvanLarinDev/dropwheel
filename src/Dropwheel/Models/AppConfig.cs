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

    /// <summary>Maximum pause between digits of a group shortcut.</summary>
    public int GroupShortcutDelayMs { get; set; } = 400;

    /// <summary>Migration marker: existing groups receive stable codes once, while a code the
    /// user later clears remains disabled.</summary>
    public bool GroupShortcutsInitialized { get; set; }

    /// <summary>Seconds of inactivity before the orb dims (0 = off).</summary>
    public int IdleFadeSeconds { get; set; } = 0;

    /// <summary>UI theme: Fluent, Dark, Light, Neon.</summary>
    public string Theme { get; set; } = "Fluent";

    /// <summary>When true, dropping a folder, app or link that already sits on the current wheel
    /// level does not add a duplicate tile; the existing tile is highlighted instead. Off by
    /// default so existing behaviour is unchanged until the user opts in.</summary>
    public bool DeduplicateTargets { get; set; }

    /// <summary>How target tiles animate when the wheel opens.</summary>
    public OpenAnimation OpenAnimation { get; set; } = OpenAnimation.Pop;

    /// <summary>Multiplier for wheel open animation speed. 1.0 = normal.</summary>
    public double OpenAnimationSpeed { get; set; } = 1.0;

    public List<TargetItem> Targets { get; set; } = new();

    /// <summary>File-type presets for the rules editor. Seeded with built-in defaults on first
    /// run, then user-editable. null (missing key) triggers reseeding on load.</summary>
    public List<FilePreset>? Presets { get; set; }

}
