using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Fluent Wheel tile: 64px rounded square on the rim.</summary>
    private FrameworkElement MakeBubble(TargetItem t)
    {
        var th = Themes.Current;
        UIElement inner = t.IsGroup
            ? new TextBlock
            {
                Text = t.Children!.Count.ToString(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(th.Label),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Width = 32,
                Height = 32,
                Source = IconService.GetIcon(t),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        var sq = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(th.TileBg),
            BorderBrush = new SolidColorBrush(
                t.IsGroup ? th.GroupBorder : t.IsSorter ? th.SorterBorder : th.TileBorder),
            BorderThickness = new Thickness(1.2),
            Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 }
        };
        var badge = new Border
        {
            Background = Brushes.MediumSpringGreen,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5, 1, 5, 1),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "⧉", FontWeight = FontWeights.Bold, FontSize = 12 }
        };
        var top = new Grid { Width = 70, Height = 66 };
        top.Children.Add(sq);
        top.Children.Add(badge);
        return WireBubble(t, badge, MakeLabel(t.Name), top, sq);
    }

    /// <summary>Tile label: light text with a dark shadow so it reads on the overlay's dark
    /// backdrop. Themes whose in-tile text is dark (Light) get a light label instead — the dark
    /// color is only right on the white tile, not under it. No pill.</summary>
    private static FrameworkElement MakeLabel(string text)
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

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
