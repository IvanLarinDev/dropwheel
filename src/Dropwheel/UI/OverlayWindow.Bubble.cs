using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private FrameworkElement MakeBubble(TargetItem t)
    {
        UIElement inner = t.IsGroup
            ? new TextBlock
            {
                Text = t.Children!.Count.ToString(), FontSize = 18,
                FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Width = 32, Height = 32, Source = IconService.GetIcon(t.Path),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        var circle = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x2A, 0x3E)),
            BorderBrush = t.IsGroup ? new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0xC4, 0xFF))
                : t.IsSorter ? new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xB8, 0x4D))
                : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(2),
            Child = inner
        };
        var badge = new Border
        {
            Background = Brushes.MediumSpringGreen, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(5, 1, 5, 1), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "⧉", FontWeight = FontWeights.Bold, FontSize = 12 }
        };
        var top = new Grid { Width = 60, Height = 58 };
        top.Children.Add(circle);
        top.Children.Add(badge);
        var label = new TextBlock
        {
            Text = t.Name, Foreground = Brushes.White, FontSize = 11,
            TextAlignment = TextAlignment.Center, MaxWidth = 70,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1 }
        };
        return WireBubble(t, badge, label, top);
    }
}
