using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dropwheel.Models;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>Всегда свободная ячейка «+»: клик — редактор новой цели,
    /// бросок папки/exe — добавить в текущий уровень (корень или открытую группу).</summary>
    private FrameworkElement MakePlusTile()
    {
        var th = Themes.Current;
        var rect = new Rectangle
        {
            RadiusX = 17, RadiusY = 17,
            Stroke = new SolidColorBrush(th.TileBorder),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeThickness = 1.4,
            Fill = Brushes.Transparent
        };
        var plus = new TextBlock
        {
            Text = "+", FontSize = 26, Foreground = new SolidColorBrush(th.Label),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var g = new Grid { Width = 64, Height = 64 };
        g.Children.Add(rect);
        g.Children.Add(plus);
        var panel = new StackPanel
        { Width = 76, AllowDrop = true, Background = Brushes.Transparent, Opacity = 0.85 };
        panel.Children.Add(new Grid { Width = 70, Height = 66, Children = { g } });
        panel.Children.Add(new TextBlock
        {
            Text = "Add", Foreground = new SolidColorBrush(th.Label), FontSize = 11.5,
            TextAlignment = TextAlignment.Center
        });
        panel.MouseEnter += (_, _) => { rect.Fill = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) => { rect.Fill = Brushes.Transparent; SetSpokeLit(panel, false); };
        panel.MouseLeftButtonUp += (_, e) =>
        {
            OpenEditor(new TargetItem { Name = "New target", Path = "" }, _currentGroup);
            e.Handled = true;
        };
        panel.DragOver += (_, e) => { e.Effects = DragDropEffects.Link; e.Handled = true; };
        panel.Drop += OnOrbDrop; // та же логика: добавить в текущий уровень
        return panel;
    }
}
