using System.Windows;
using System.Windows.Controls;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private StackPanel WireBubble(TargetItem t, Border badge, TextBlock label, Grid top)
    {
        var panel = new StackPanel
        {
            Width = 70, Tag = t,
            AllowDrop = t.IsFolder,
            Opacity = t.Exists ? 1.0 : 0.4, // битый путь — цель серая
            Background = System.Windows.Media.Brushes.Transparent
        };
        panel.Children.Add(top);
        panel.Children.Add(label);

        panel.MouseLeftButtonUp += (_, e) =>
        { LaunchService.Launch(t); CloseCloud(); e.Handled = true; };
        panel.MouseRightButtonUp += (_, e) =>
        { OpenEditor(t); e.Handled = true; };
        panel.DragOver  += (_, e) => OnBubbleDragOver(t, badge, e);
        panel.DragLeave += (_, _) => badge.Visibility = Visibility.Collapsed;
        panel.Drop      += (_, e) => OnBubbleDrop(t, badge, e);
        return panel;
    }
}
