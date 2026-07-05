using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Бабл «←» — возврат из группы на корневой уровень.
    /// Работает и кликом, и наведением drag'а.</summary>
    private FrameworkElement MakeBackBubble()
    {
        var circle = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2A, 0x3A, 0x55)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7C, 0xC4, 0xFF)),
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = "←", FontSize = 22, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var panel = new StackPanel
        { Width = 70, AllowDrop = true, Background = Brushes.Transparent };
        panel.Children.Add(circle);
        panel.Children.Add(new TextBlock
        {
            Text = "Back", Foreground = Brushes.White, FontSize = 11,
            TextAlignment = TextAlignment.Center
        });
        panel.MouseLeftButtonUp += (_, e) => { EnterGroup(null); e.Handled = true; };
        panel.DragOver += (_, e) =>
        { StartGroupHover(null, back: true); e.Effects = DragDropEffects.Scroll; e.Handled = true; };
        panel.DragLeave += (_, _) => _groupHover?.Stop();
        return panel;
    }
}
