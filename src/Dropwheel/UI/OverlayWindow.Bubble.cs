using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Плитка Fluent Wheel: скруглённый квадрат 64px на ободе.</summary>
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
        var label = new TextBlock
        {
            Text = t.Name, Foreground = new SolidColorBrush(th.Label), FontSize = 11.5,
            TextAlignment = TextAlignment.Center, MaxWidth = 76,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.8 }
        };
        return WireBubble(t, badge, label, top, sq);
    }
}
