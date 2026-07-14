using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Fluent Wheel tile: 64px rounded square on the rim.</summary>
    private FrameworkElement MakeBubble(TargetItem t)
    {
        var th = Themes.Current;
        var customBorder = ParseTileColor(t.TileColor);
        UIElement inner;
        if (!string.IsNullOrWhiteSpace(t.Emoji))
        {
            // A chosen emoji wins over both the file icon and a group's count — it's the tile's face.
            // A group keeps its shortcut badge, so the count is the only thing the emoji replaces.
            // Text-presentation emoji (hearts, checks) take the foreground: the tile's own colour when
            // set, the theme's in-tile text otherwise — a black default is invisible on dark tiles.
            // Full-colour emoji ignore the foreground and stay as they are.
            inner = new TextBlock
            {
                Text = t.Emoji,
                FontSize = 30,
                Foreground = new SolidColorBrush(customBorder ?? th.Label),
                Opacity = t.Exists ? 1.0 : 0.45,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else if (t.IsGroup)
        {
            inner = new TextBlock
            {
                // A non-empty group shows how many targets it holds; an empty one shows a container
                // glyph instead of a bare "0", which read like a bug.
                Text = t.Children!.Count > 0 ? t.Children.Count.ToString() : "▤",
                FontSize = t.Children.Count > 0 ? 20 : 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(th.Label),
                Opacity = t.Children.Count > 0 ? 1.0 : 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            inner = new Image
            {
                Width = 32,
                Height = 32,
                Source = t.Exists ? IconService.GetIcon(t) : Desaturate(IconService.GetIcon(t)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        var themeBorder = t.IsGroup ? th.GroupBorder : t.IsSorter ? th.SorterBorder : th.TileBorder;
        var sq = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(th.TileBg),
            BorderBrush = new SolidColorBrush(customBorder ?? themeBorder),
            BorderThickness = new Thickness(customBorder != null ? 2.2 : 1.2),
            Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 }
        };
        var badge = new Border
        {
            Background = Palettes.Success,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5, 1, 5, 1),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "Copy", FontWeight = FontWeights.Bold, FontSize = 11 }
        };
        var confidence = MakeConfidenceOverlay();
        var top = new Grid { Width = 70, Height = 66 };
        top.Children.Add(sq);
        top.Children.Add(confidence.Ring);
        top.Children.Add(badge);
        if (t.IsGroup && GroupShortcutSequence.IsValidCode(t.GroupCode))
        {
            top.Children.Add(new Border
            {
                Background = new SolidColorBrush(th.Accent),
                CornerRadius = new CornerRadius(9),
                MinWidth = 20,
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = t.GroupCode,
                    Foreground = new SolidColorBrush(OnAccentText(th.Accent)),
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                },
            });
        }
        if (!t.IsGroup && !t.Exists) top.Children.Add(MissingBadge());
        top.Children.Add(confidence.Chip);
        if (TileTooltip(t) is { Length: > 0 } tip) sq.ToolTip = tip;
        var label = MakeLabel(t.Name);
        var tile = WireBubble(t, badge, label, top, sq);
        System.Windows.Automation.AutomationProperties.SetName(tile, AccessibleName(t));
        RegisterConfidenceVisuals(
            tile,
            confidence.Ring,
            confidence.Chip,
            confidence.ChipText,
            label,
            AccessibleName(t),
            () =>
            {
                if (t.IsGroup) EnterGroup(t);
                else if (!t.Exists) ShowMissingMenu(t);
                else { LaunchService.Launch(t); CloseCloud(); }
            },
            KeyboardStatus(t),
            badge,
            target: t);
        return tile;
    }

    /// <summary>The hover tooltip for a tile: a link target's URL, a folder/app/sorter target's full path,
    /// or a short description for a group (which has no path of its own). Lets two same-named tiles from
    /// different places be told apart on hover.</summary>
    internal static string TileTooltip(TargetItem t)
    {
        if (t.IsGroup)
            return t.Children!.Count > 0 ? $"Group · {t.Children.Count} target(s)" : "Group";
        return t.SourceUrl ?? t.Path;
    }

    /// <summary>A tile's custom border colour parsed from its hex string, or null when unset or invalid
    /// (so a hand-edited config with a bad colour just falls back to the theme border, never crashes).</summary>
    internal static Color? ParseTileColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>A small warning mark in the tile's bottom-right corner, shown when the target's folder
    /// or file is gone. It reads the problem without dimming the whole tile (which looked like a bug).</summary>
    private static Border MissingBadge() => new()
    {
        Background = Palettes.Warning,
        CornerRadius = new CornerRadius(8),
        MinWidth = 16,
        Height = 16,
        Padding = new Thickness(4, 0, 4, 0),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Child = new TextBlock
        {
            Text = "!",
            Foreground = new SolidColorBrush(OnAccentText(Palettes.Current.Warning)),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Screen-reader label for a wheel tile: the target name plus a hint of what it is, since
    /// the tile itself is drawn from plain shapes and exposes no text of its own to accessibility tools.</summary>
    private static string AccessibleName(TargetItem t) =>
        t.IsGroup ? $"Group {t.Name}, {t.Children!.Count} items"
        : t.IsSorter ? $"Sorter {t.Name}"
        : t.Name;

    /// <summary>Tile label: light text with a dark shadow so it reads on the overlay's dark
    /// backdrop. Themes whose in-tile text is dark (Light) get a light label instead — the dark
    /// color is only right on the white tile, not under it. No pill.</summary>
    private static TextBlock MakeLabel(string text)
    {
        var th = Themes.Current;
        var color = Luminance(th.Label) < 0.5 ? Color.FromRgb(0xEC, 0xF1, 0xF7) : th.Label;
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(color),
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 88,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.85 },
        };
    }

    private static string KeyboardStatus(TargetItem t) =>
        t.IsGroup ? $"Group {t.Name}. Press Enter to open."
        : !t.Exists ? $"{t.Name}. Target is missing. Press Enter to locate or remove."
        : LaunchService.IsRunTarget(t) ? $"{t.Name}. Press Enter to launch."
        : $"{t.Name}. Press Enter to open.";

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Grayscale version of an icon, used for a broken target so the tile still shows what it
    /// is but reads as inactive. Non-bitmap sources (rare) are returned unchanged.</summary>
    private static ImageSource? Desaturate(ImageSource? source)
    {
        if (source is not BitmapSource bitmap) return source;
        var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray32Float, null, 0);
        gray.Freeze();
        return gray;
    }

    /// <summary>Text color that reads on top of a filled badge: near-white on dark fills,
    /// near-black on light ones, so the digit stays legible whatever the theme's accent is.</summary>
    private static Color OnAccentText(Color bg) =>
        Luminance(bg) < 0.5 ? Color.FromRgb(0xF0, 0xF4, 0xF8) : Color.FromRgb(0x0A, 0x0E, 0x14);
}
