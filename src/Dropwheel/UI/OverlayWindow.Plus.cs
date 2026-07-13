using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dropwheel.Models;

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
            Width = 64,
            Height = 64,
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
        var confidence = MakeConfidenceOverlay();
        var g = new Grid { Width = 70, Height = 66 };
        g.Children.Add(rect);
        g.Children.Add(plus);
        g.Children.Add(confidence.Ring);
        g.Children.Add(confidence.Chip);
        var panel = new StackPanel
        { Width = 76, AllowDrop = true, Background = Brushes.Transparent, Opacity = 0.85 };
        panel.Children.Add(g);
        bool hasExplorerSelection = _explorerBridgeFiles is { Length: > 0 };
        var label = MakeLabel(hasExplorerSelection ? "Add selected" : "Add");
        panel.Children.Add(label);
        panel.MouseEnter += (_, _) => { rect.Fill = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) => { rect.Fill = Brushes.Transparent; SetSpokeLit(panel, false); };
        panel.MouseLeftButtonUp += (_, e) =>
        {
            if (TryAddExplorerBridgeTargets())
            {
                e.Handled = true;
                return;
            }
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
            e.Effects = CanAddTarget(e.Data) ? AddTargetDropEffect(e) : DragDropEffects.None;
            ShowGeneralConfidence(
                panel,
                e.Effects == DragDropEffects.None ? "Can't" : "Add",
                e.Effects == DragDropEffects.None
                    ? "This payload cannot be added as a target."
                    : "Drop to add a target to the current wheel level.",
                e.Effects == DragDropEffects.None ? ConfidenceTone.Danger : ConfidenceTone.Info,
                e.Effects != DragDropEffects.None,
                activeLabelText: e.Effects == DragDropEffects.None ? "Cannot add" : "Add target");
            e.Handled = true;
        };
        panel.DragLeave += (_, _) => ClearConfidenceTarget(panel);
        panel.Drop += (_, e) =>
        {
            ClearConfidenceTarget(panel);
            if (IsTileReorderDrag(e))
            {
                OnTileReorderDropToEnd(e);
                return;
            }
            OnOrbDrop(panel, e); // same logic: add to the current level
        };
        System.Windows.Automation.AutomationProperties.SetName(panel, hasExplorerSelection ? "Add selected" : "Add target");
        RegisterConfidenceVisuals(
            panel,
            confidence.Ring,
            confidence.Chip,
            confidence.ChipText,
            label,
            hasExplorerSelection ? "Add selected" : "Add target",
            () =>
            {
                if (!TryAddExplorerBridgeTargets())
                    OpenEditor(new TargetItem { Name = "New target", Path = "" }, _currentGroup);
            },
            hasExplorerSelection
                ? "Add selected Explorer item. Press Enter to add it as a target."
                : "Add target. Press Enter to create a new target.");
        return panel;
    }
}
