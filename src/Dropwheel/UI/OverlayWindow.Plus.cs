using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    /// <summary>The always-free "+" cell: click opens the new-target editor,
    /// dropping a folder/exe adds it to the current level (root or open group).</summary>
    private FrameworkElement MakePlusTile()
    {
        var th = Themes.Current;
        var rect = new Rectangle
        {
            RadiusX = 17,
            RadiusY = 17,
            Stroke = new SolidColorBrush(th.TileBorder),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            StrokeThickness = 1.4,
            Fill = Brushes.Transparent
        };
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = 26,
            Foreground = new SolidColorBrush(th.Label),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var g = new Grid { Width = 64, Height = 64 };
        g.Children.Add(rect);
        g.Children.Add(plus);
        var panel = new StackPanel
        { Width = 76, AllowDrop = true, Background = Brushes.Transparent, Opacity = 0.85 };
        panel.Children.Add(new Grid { Width = 70, Height = 66, Children = { g } });
        panel.Children.Add(MakeLabel("Add"));
        panel.MouseEnter += (_, _) => { rect.Fill = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) => { rect.Fill = Brushes.Transparent; SetSpokeLit(panel, false); };
        panel.MouseLeftButtonUp += (_, e) =>
        {
            OpenEditor(new TargetItem { Name = "New target", Path = "" }, _currentGroup);
            e.Handled = true;
        };
        panel.DragOver += (_, e) =>
        {
            if (IsTileReorderDrag(e))
            {
                e.Effects = CanReorderToEnd(e) ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                return;
            }
            bool canAdd = CanAddTarget(e.Data);
            e.Effects = canAdd ? AddTargetDropEffect(e) : DragDropEffects.None;
            SetPinRing(canAdd && AddTargetIntent.ShouldPin(e.KeyStates));
            e.Handled = true;
        };
        panel.DragLeave += (_, _) => SetPinRing(false);
        panel.Drop += (_, e) =>
        {
            if (IsTileReorderDrag(e))
            {
                OnTileReorderDropToEnd(e);
                return;
            }
            OnOrbDrop(panel, e); // same logic: add to the current level
        };
        return panel;
    }
}
