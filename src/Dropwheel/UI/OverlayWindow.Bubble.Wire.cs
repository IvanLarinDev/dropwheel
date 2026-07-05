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
            AllowDrop = t.IsFolder || t.IsGroup,
            Opacity = t.Exists ? 1.0 : 0.4, // битый путь — цель серая
            Background = System.Windows.Media.Brushes.Transparent
        };
        panel.Children.Add(top);
        panel.Children.Add(label);

        panel.MouseLeftButtonUp += (_, e) =>
        {
            if (t.IsGroup) EnterGroup(t);
            else { LaunchService.Launch(t); CloseCloud(); }
            e.Handled = true;
        };
        panel.MouseRightButtonUp += (_, e) => { OpenEditor(t); e.Handled = true; };

        panel.DragOver += (_, e) =>
        {
            if (t.IsGroup)
            {
                // быстрый бросок = добавить в группу; задержка 500 мс = раскрыть её
                StartGroupHover(t, back: false);
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            else OnBubbleDragOver(t, badge, e);
        };
        panel.DragLeave += (_, _) =>
        {
            badge.Visibility = Visibility.Collapsed;
            if (t.IsGroup) _groupHover?.Stop();
        };
        panel.Drop += (_, e) =>
        {
            if (t.IsGroup) OnGroupDrop(t, e);
            else OnBubbleDrop(t, badge, e);
        };
        return panel;
    }
}
