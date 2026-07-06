using System.Windows.Media;
using Dropwheel.Services;

namespace Dropwheel.UI;

public record Theme(
    Color TileBg, Color TileHot, Color TileBorder, Color Label,
    Color Rim, Color Spoke, Color Accent,
    Color HubBg, Color HubBorder,
    Color GroupBorder, Color SorterBorder,
    Color LabelBg); // label backdrop; A=0 — no backdrop (shadow instead)

public static class Themes
{
    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static readonly Dictionary<string, Theme> All = new()
    {
        ["Fluent"] = new(
            C(0x1F, 0xFF, 0xFF, 0xFF), C(0x3D, 0xFF, 0xFF, 0xFF), C(0x3D, 0xFF, 0xFF, 0xFF),
            C(0xFF, 0xDB, 0xE7, 0xF5),
            C(0x14, 0xFF, 0xFF, 0xFF), C(0x30, 0xFF, 0xFF, 0xFF), C(0xFF, 0x4D, 0xA3, 0xFF),
            C(0x24, 0xFF, 0xFF, 0xFF), C(0x4D, 0xFF, 0xFF, 0xFF),
            C(0x8C, 0x7C, 0xC4, 0xFF), C(0x8C, 0xFF, 0xB8, 0x4D),
            C(0x00, 0x00, 0x00, 0x00)),

        ["Dark"] = new(
            C(0xF0, 0x20, 0x26, 0x30), C(0xF0, 0x2E, 0x38, 0x48), C(0x38, 0xFF, 0xFF, 0xFF),
            C(0xFF, 0xC9, 0xD2, 0xDE),
            C(0x1A, 0x8A, 0x96, 0xA8), C(0x26, 0xC0, 0xC8, 0xD4), C(0xFF, 0x9D, 0xB2, 0xCC),
            C(0xF0, 0x1A, 0x20, 0x2A), C(0x4D, 0xC0, 0xC8, 0xD4),
            C(0x8C, 0x8A, 0xB0, 0xFF), C(0x8C, 0xE8, 0xA6, 0x48),
            C(0x00, 0x00, 0x00, 0x00)),

        ["Light"] = new(
            C(0xE8, 0xFF, 0xFF, 0xFF), C(0xFF, 0xFF, 0xFF, 0xFF), C(0x30, 0x00, 0x00, 0x00),
            C(0xFF, 0x1A, 0x24, 0x30),
            C(0x30, 0xFF, 0xFF, 0xFF), C(0x40, 0x00, 0x00, 0x00), C(0xFF, 0x0B, 0x62, 0xC6),
            C(0xF0, 0xFF, 0xFF, 0xFF), C(0x40, 0x00, 0x00, 0x00),
            C(0xC8, 0x3D, 0x7D, 0xD6), C(0xC8, 0xD8, 0x8A, 0x1E),
            C(0xE6, 0xFF, 0xFF, 0xFF)),

        ["Neon"] = new(
            C(0xE6, 0x08, 0x10, 0x1C), C(0xE6, 0x0E, 0x1E, 0x30), C(0x8C, 0x29, 0xD8, 0xFF),
            C(0xFF, 0x8F, 0xDC, 0xEF),
            C(0x18, 0x29, 0xD8, 0xFF), C(0x33, 0x29, 0xD8, 0xFF), C(0xFF, 0x59, 0xF5, 0xFF),
            C(0xE6, 0x06, 0x14, 0x22), C(0x8C, 0x29, 0xD8, 0xFF),
            C(0x9C, 0xB4, 0x8C, 0xFF), C(0x9C, 0xFF, 0x7A, 0xD8),
            C(0x00, 0x00, 0x00, 0x00)),
    };

    public static Theme Current =>
        All.TryGetValue(TargetStore.Config.Theme, out var t) ? t : All["Fluent"];

    /// <summary>Whether the current theme uses dark window chrome (Dark or Neon).</summary>
    public static bool IsWindowDark => Palettes.Current.Dark;

    /// <summary>Applies the theme to a window: Fluent light/dark chrome as a base, then the theme's
    /// own background and text so the dialog matches the palette. Stock controls tint against the
    /// background; our custom UI reads brushes from <see cref="Palettes"/> directly.
    /// paintBackground=false keeps a transparent window (the overlay) see-through.</summary>
    public static void ApplyWindow(System.Windows.Window w, bool paintBackground = true)
    {
        var p = Palettes.Current;
        w.ThemeMode = p.Dark ? System.Windows.ThemeMode.Dark : System.Windows.ThemeMode.Light;
        if (paintBackground) w.Background = Palettes.Brush(p.WindowBg);
        w.Foreground = Palettes.Brush(p.Text);
        ApplyAccent(w, p);
    }

    /// <summary>Repoints the Fluent theme's accent brushes at the theme accent so stock controls
    /// (focus rings, selection, checkboxes, primary buttons) match the theme instead of the OS accent.</summary>
    private static void ApplyAccent(System.Windows.Window w, Palette p)
    {
        var accent = Palettes.Brush(p.Accent);
        var onAccent = Palettes.Brush(p.AccentText);
        w.Resources["AccentFillColorDefaultBrush"] = accent;
        w.Resources["AccentFillColorSecondaryBrush"] = Palettes.Brush(Palettes.Alpha(p.Accent, 0xE6));
        w.Resources["AccentFillColorTertiaryBrush"] = Palettes.Brush(Palettes.Alpha(p.Accent, 0xCC));
        w.Resources["AccentFillColorSelectedTextBackgroundBrush"] = accent;
        w.Resources["AccentTextFillColorPrimaryBrush"] = accent;
        w.Resources["AccentTextFillColorSecondaryBrush"] = accent;
        w.Resources["AccentTextFillColorTertiaryBrush"] = accent;
        w.Resources["TextOnAccentFillColorPrimaryBrush"] = onAccent;
        w.Resources["TextOnAccentFillColorSecondaryBrush"] = onAccent;
        w.Resources[System.Windows.SystemColors.AccentColorBrushKey] = accent;
    }

    /// <summary>Colors a context menu from the palette via the themed style, killing the default
    /// white icon gutter and routing hover through the accent. Used for the orb menu, whose window
    /// (the transparent overlay) must not carry ThemeMode.</summary>
    public static void ApplyMenu(System.Windows.Controls.ContextMenu m)
    {
        var p = Palettes.Current;
        m.Resources["MenuBg"] = Palettes.Brush(p.Surface);
        m.Resources["MenuBorder"] = Palettes.Brush(p.Border);
        m.Resources["MenuText"] = Palettes.Brush(p.Text);
        m.Resources["MenuTextMuted"] = Palettes.Brush(p.TextMuted);
        m.Resources["MenuHover"] = Palettes.Brush(Palettes.Alpha(p.Accent, 0x4D));
        if (System.Windows.Application.Current?.TryFindResource("ThemedContextMenu") is System.Windows.Style menuStyle)
            m.Style = menuStyle;
        if (System.Windows.Application.Current?.TryFindResource("ThemedMenuItem") is System.Windows.Style itemStyle)
            foreach (var item in m.Items)
                if (item is System.Windows.Controls.MenuItem mi) mi.Style = itemStyle;
    }
}
