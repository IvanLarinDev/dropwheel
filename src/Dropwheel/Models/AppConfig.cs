namespace Dropwheel.Models;

public class AppConfig
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";
    public const int CurrentOnboardingVersion = 1;

    /// <summary>Completed onboarding revision. New profiles override this to zero, while configs
    /// written before onboarding existed retain the current value and are not interrupted on upgrade.</summary>
    public int OnboardingVersion { get; set; } = CurrentOnboardingVersion;

    public DropAction GlobalAction { get; set; } = DropAction.Copy;

    // Orb center (DIP, virtual screen); NaN = default (right edge of primary).
    // -1 is no good as a marker: a monitor left of the primary yields negative coordinates.
    public double OrbX { get; set; } = double.NaN;
    public double OrbY { get; set; } = double.NaN;

    public double OrbOpacity { get; set; } = 0.8;
    public int HoverDelayMs { get; set; } = 250;
    public string Hotkey { get; set; } = DefaultHotkey;

    /// <summary>Optional second global hotkey that opens the wheel at the orb's home position instead of
    /// at the cursor. Empty means no second hotkey.</summary>
    public string HotkeyAtOrb { get; set; } = "";

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

    /// <summary>How a level lays out its tiles once they exceed the main ring. Defaults to None —
    /// the classic single ring — so existing wheels are unchanged until the user opts in.</summary>
    public OverflowLayout OverflowLayout { get; set; } = OverflowLayout.None;

    /// <summary>How many tiles the main ring holds before the extra ring appears (ignored when the
    /// layout is None). Clamped to a sensible range on load.</summary>
    public int OverflowThreshold { get; set; } = 9;

    /// <summary>Multiplier for wheel open animation speed. 1.0 = normal.</summary>
    public double OpenAnimationSpeed { get; set; } = 1.0;

    /// <summary>Overall wheel size multiplier: scales the ring, tiles, orb and labels together. 1.0 =
    /// the classic size. Clamped to a sane range on load.</summary>
    public double WheelScale { get; set; } = 1.0;

    public List<TargetItem> Targets { get; set; } = new();

    /// <summary>File-type presets for the rules editor. Seeded with built-in defaults on first
    /// run, then user-editable. null (missing key) triggers reseeding on load.</summary>
    public List<FilePreset>? Presets { get; set; }

    /// <summary>How many times each one-off hint has been shown, keyed by hint id. Housekeeping, not a
    /// user setting — it just stops a tip (fullscreen notice, hidden gestures) from repeating forever.</summary>
    public Dictionary<string, int> HintShows { get; set; } = new();

    /// <summary>Template for the file name of dropped text, without extension. Uses the same ${name}
    /// tokens as sorter destinations: ${slug} (first line of the text), ${date} ${time} ${year} ${month}
    /// ${day}. Empty (the default) keeps the classic text_yyyy-MM-dd_HH-mm-ss name.</summary>
    public string TextFileNameTemplate { get; set; } = "";

    /// <summary>After a successful drop, copy the destination path to the clipboard so it can be pasted
    /// straight away. The path is the created file for a single saved file, or the destination folder
    /// otherwise. Off by default.</summary>
    public bool CopyDestinationToClipboard { get; set; }

    /// <summary>Base seconds a toast stays up; a Danger or undoable toast lasts twice as long. Clamped to
    /// a sane range on use. Default 4.</summary>
    public int ToastSeconds { get; set; } = 4;

    /// <summary>Play a short system sound for Warning and Danger toasts. Off by default.</summary>
    public bool ToastSound { get; set; }

}
