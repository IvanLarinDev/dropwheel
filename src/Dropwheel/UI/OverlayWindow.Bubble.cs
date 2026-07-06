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
                Text = t.Children!.Count.ToString(), FontSize = 20,
                FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(th.Label),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Width = 32, Height = 32, Source = IconService.GetIcon(t.Path),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        var sq = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(th.TileBg),
            BorderBrush = new SolidColorBrush(
                t.IsGroup ? th.GroupBorder : t.IsSorter ? th.SorterBorder : th.TileBorder),
            BorderThickness = new Thickness(1.2),
            Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 }
        };
        var badge = new Border
        {
            Background = Brushes.MediumSpringGreen, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5, 1, 5, 1), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "⧉", FontWeight = FontWeights.Bold, FontSize = 12 }
        };
        var top = new Grid { Width = 70, Height = 66 };
        top.Children.Add(sq);
        top.Children.Add(badge);
        return WireBubble(t, badge, MakeLabel(t.Name), top, sq);
    }

    /// <summary>Tile label: drop-shadowed text in dark themes, a pill background
    /// in the light theme so it stays readable on any wallpaper.</summary>
    private static FrameworkElement MakeLabel(string text)
    {
        var th = Themes.Current;
        var tb = new TextBlock
        {
            Text = text, Foreground = new SolidColorBrush(th.Label), FontSize = 11.5,
            TextAlignment = TextAlignment.Center, MaxWidth = 76,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (th.LabelBg.A == 0)
        {
            tb.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.8 };
            return tb;
        }
        return new Border
        {
            Background = new SolidColorBrush(th.LabelBg),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 1, 7, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.35 },
            Child = tb
        };
    }
}
