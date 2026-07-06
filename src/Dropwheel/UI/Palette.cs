using System.Windows.Media;
using Dropwheel.Services;

namespace Dropwheel.UI;

/// <summary>Widget colors for a theme: window chrome, surfaces, text, and accent. Separate from
/// the wheel-focused Theme record so dialogs, the editor, and menus can match the theme too.</summary>
public record Palette(
    Color WindowBg, Color Surface, Color Text, Color TextMuted,
    Color Border, Color Accent, Color AccentText, Color Selection,
    bool Dark);

public static class Palettes
{
    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static readonly Dictionary<string, Palette> All = new()
    {
        ["Fluent"] = new(
            WindowBg: C(0xFF, 0xF5, 0xF7, 0xFA), Surface: C(0xFF, 0xFF, 0xFF, 0xFF),
            Text: C(0xFF, 0x1B, 0x24, 0x30), TextMuted: C(0xFF, 0x6B, 0x76, 0x86),
            Border: C(0xFF, 0xD6, 0xDC, 0xE4), Accent: C(0xFF, 0x2C, 0x7B, 0xE5),
            AccentText: C(0xFF, 0xFF, 0xFF, 0xFF), Selection: C(0x18, 0x2C, 0x7B, 0xE5), Dark: false),

        ["Dark"] = new(
            WindowBg: C(0xFF, 0x20, 0x26, 0x2E), Surface: C(0xFF, 0x2A, 0x31, 0x3B),
            Text: C(0xFF, 0xC9, 0xD2, 0xDE), TextMuted: C(0xFF, 0x8A, 0x96, 0xA8),
            Border: C(0xFF, 0x3A, 0x42, 0x4E), Accent: C(0xFF, 0x6F, 0xA8, 0xFF),
            AccentText: C(0xFF, 0x0A, 0x0E, 0x14), Selection: C(0x2A, 0x6F, 0xA8, 0xFF), Dark: true),

        ["Light"] = new(
            WindowBg: C(0xFF, 0xF4, 0xF6, 0xF9), Surface: C(0xFF, 0xFF, 0xFF, 0xFF),
            Text: C(0xFF, 0x1A, 0x24, 0x30), TextMuted: C(0xFF, 0x66, 0x70, 0x80),
            Border: C(0xFF, 0xD0, 0xD6, 0xDE), Accent: C(0xFF, 0x0B, 0x62, 0xC6),
            AccentText: C(0xFF, 0xFF, 0xFF, 0xFF), Selection: C(0x18, 0x0B, 0x62, 0xC6), Dark: false),

        ["Neon"] = new(
            WindowBg: C(0xFF, 0x06, 0x14, 0x22), Surface: C(0xFF, 0x0A, 0x1E, 0x30),
            Text: C(0xFF, 0xCF, 0xEF, 0xF6), TextMuted: C(0xFF, 0x6F, 0xA6, 0xB4),
            Border: C(0xFF, 0x14, 0x38, 0x4A), Accent: C(0xFF, 0x35, 0xD6, 0xFF),
            AccentText: C(0xFF, 0x04, 0x12, 0x1A), Selection: C(0x30, 0x29, 0xD8, 0xFF), Dark: true),
    };

    public static Palette Current =>
        All.TryGetValue(TargetStore.Config.Theme, out var p) ? p : All["Fluent"];

    private static readonly Dictionary<Color, Brush> Cache = new();

    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Frozen brush for a color, cached so repeated UI builds do not re-allocate.</summary>
    public static Brush Brush(Color c)
    {
        if (Cache.TryGetValue(c, out var b)) return b;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        Cache[c] = brush;
        return brush;
    }

    public static Brush WindowBg => Brush(Current.WindowBg);
    public static Brush Surface => Brush(Current.Surface);
    public static Brush Text => Brush(Current.Text);
    public static Brush TextMuted => Brush(Current.TextMuted);
    public static Brush Border => Brush(Current.Border);
    public static Brush Accent => Brush(Current.Accent);
    public static Brush Selection => Brush(Current.Selection);
}
