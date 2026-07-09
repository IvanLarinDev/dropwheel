using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private const string TileReorderFormat = "Dropwheel.TileReorder.Target";
    private Point _tileDragStart;
    private TargetItem? _tileDragCandidate;
    private bool _suppressTileClick;

    private StackPanel WireBubble(TargetItem t, Border badge, FrameworkElement label, Grid top, Border sq)
    {
        var th = Themes.Current;
        var panel = new StackPanel
        {
            Width = 76,
            Tag = t,
            AllowDrop = true,
            Opacity = t.Exists ? 1.0 : 0.4,
            Background = Brushes.Transparent
        };
        panel.Children.Add(top);
        panel.Children.Add(label);

        panel.MouseEnter += (_, _) =>
        { sq.Background = new SolidColorBrush(th.TileHot); SetSpokeLit(panel, true); };
        panel.MouseLeave += (_, _) =>
        { sq.Background = new SolidColorBrush(th.TileBg); SetSpokeLit(panel, false); };

        panel.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _tileDragCandidate = t;
            _tileDragStart = e.GetPosition(this);
            _suppressTileClick = false;
        };
        panel.MouseMove += (_, e) => StartTileReorderDrag(panel, t, e);
        panel.MouseLeftButtonUp += (_, e) =>
        {
            _tileDragCandidate = null;
            if (_suppressTileClick)
            {
                _suppressTileClick = false;
                e.Handled = true;
                return;
            }
            if (t.IsGroup) EnterGroup(t);
            else { LaunchService.Launch(t); CloseCloud(); }
            e.Handled = true;
        };
        panel.MouseRightButtonUp += (_, e) => { OpenEditor(t); e.Handled = true; };
        panel.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle || !t.IsSorter) return;
            SortTargetFolderNow(t);
            CloseCloud();
            e.Handled = true;
        };

        panel.DragOver += (_, e) =>
        {
            SetSpokeLit(panel, true);
            if (IsTileReorderDrag(e))
            {
                OnTileReorderDragOver(t, badge, e);
                return;
            }
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
            if (IsTileReorderDrag(e))
            {
                OnTileReorderDrop(t, badge, e);
                return;
            }
            if (t.IsGroup) OnGroupDrop(t, e);
            else OnBubbleDrop(t, badge, e);
        };
        return panel;
    }

    private void StartTileReorderDrag(FrameworkElement sourceElement, TargetItem source, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !ReferenceEquals(_tileDragCandidate, source)) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _tileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _tileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _suppressTileClick = true;
        _tileDragCandidate = null;
        var data = new DataObject();
        data.SetData(TileReorderFormat, source);
        DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Move);
    }

    private bool IsTileReorderDrag(DragEventArgs e) => e.Data.GetDataPresent(TileReorderFormat);

    private TargetItem? TileReorderSource(DragEventArgs e) =>
        e.Data.GetData(TileReorderFormat) as TargetItem;

    private bool CanReorderBefore(TargetItem target, DragEventArgs e)
    {
        var source = TileReorderSource(e);
        var level = CurrentLevelTargets();
        return source != null
            && !ReferenceEquals(source, target)
            && level.Contains(source)
            && level.Contains(target);
    }

    private bool CanReorderToEnd(DragEventArgs e)
    {
        var source = TileReorderSource(e);
        return source != null && CurrentLevelTargets().Contains(source);
    }

    private void OnTileReorderDragOver(TargetItem target, Border badge, DragEventArgs e)
    {
        if (!CanReorderBefore(target, e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        ((TextBlock)badge.Child).Text = "↕";
        badge.Background = Brushes.DeepSkyBlue;
        badge.Visibility = Visibility.Visible;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTileReorderDrop(TargetItem target, Border badge, DragEventArgs e)
    {
        badge.Visibility = Visibility.Collapsed;
        var source = TileReorderSource(e);
        if (source != null && TargetStore.MoveTileBefore(CurrentLevelTargets(), source, target))
        {
            TargetStore.Save();
            BuildCloud();
            ShowToast($"Moved {source.Name}");
        }
        e.Handled = true;
    }

    private void OnTileReorderDropToEnd(DragEventArgs e)
    {
        var source = TileReorderSource(e);
        if (source != null && TargetStore.MoveTileToEnd(CurrentLevelTargets(), source))
        {
            TargetStore.Save();
            BuildCloud();
            ShowToast($"Moved {source.Name}");
        }
        e.Handled = true;
    }
}
