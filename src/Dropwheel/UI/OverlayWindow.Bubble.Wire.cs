using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private StackPanel WireBubble(TargetItem t, Border badge, FrameworkElement label, Grid top, Border sq)
    {
        var th = Themes.Current;
        var panel = new StackPanel
        {
            Width = 76, Tag = t,
            AllowDrop = t.IsFolder || t.IsGroup,
            Opacity = t.Exists ? 1.0 : 0.4,
            Background = Brushes.Transparent
        };
        panel.Children.Add(top);
        panel.Children.Add(label);

        panel.MouseEnter += (_, _) =>
        { sq.Background = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) =>
        { sq.Background = new SolidColorBrush(th.TileBg); SetSpokeLit(panel, false); };

        panel.MouseLeftButtonUp += (_, e) =>
        {
            if (t.IsGroup) EnterGroup(t);
            else { LaunchService.Launch(t); CloseCloud(); }
            e.Handled = true;
        };
        panel.MouseRightButtonUp += (_, e) => { OpenEditor(t); e.Handled = true; };

        panel.DragOver += (_, e) =>
        {
            SetSpokeLit(panel, true);
            if (t.IsGroup)
            {
                StartGroupHover(t, back: false);
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            else OnBubbleDragOver(t, badge, e);
        };
        panel.DragLeave += (_, _) =>
        {
            badge.Visibility = Visibility.Collapsed;
            SetSpokeLit(panel, false);
            if (t.IsGroup) _groupHover?.Stop();
        };
        panel.Drop += (_, e) =>
        {
            SetSpokeLit(panel, false);
            if (t.IsGroup) OnGroupDrop(t, e);
            else OnBubbleDrop(t, badge, e);
        };
        return panel;
    }
}
