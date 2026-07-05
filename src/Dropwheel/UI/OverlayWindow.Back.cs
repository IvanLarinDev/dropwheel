using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Плитка «←» — возврат из группы на корневой уровень.</summary>
    private FrameworkElement MakeBackBubble()
    {
        var th = Themes.Current;
        var sq = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(th.TileBg),
            BorderBrush = new SolidColorBrush(th.GroupBorder),
            BorderThickness = new Thickness(1.2),
            Child = new TextBlock
            {
                Text = "←", FontSize = 24, Foreground = new SolidColorBrush(th.Label),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var panel = new StackPanel
        { Width = 76, AllowDrop = true, Background = Brushes.Transparent };
        panel.Children.Add(new Grid { Width = 70, Height = 66, Children = { sq } });
        panel.Children.Add(new TextBlock
        {
            Text = "Back", Foreground = new SolidColorBrush(th.Label), FontSize = 11.5,
            TextAlignment = TextAlignment.Center
        });
        panel.MouseEnter += (_, _) => { sq.Background = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) => { sq.Background = new SolidColorBrush(th.TileBg); SetSpokeLit(panel, false); };
        panel.MouseLeftButtonUp += (_, e) => { EnterGroup(null); e.Handled = true; };
        panel.DragOver += (_, e) =>
        { StartGroupHover(null, back: true); e.Effects = DragDropEffects.Scroll; e.Handled = true; };
        panel.DragLeave += (_, _) => _groupHover?.Stop();
        return panel;
    }
}
