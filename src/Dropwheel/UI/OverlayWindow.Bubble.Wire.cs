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
    private IList<TargetItem>? _tileDragLevel;
    private TargetItem[]? _tileDragOriginalItems;
    private TargetItem[]? _tileDragOriginalDisplay;
    private int?[]? _tileDragOriginalPositions;
    private bool _tileReorderCommitted;

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
            else { LaunchService.Launch(t); CloseCloud("launch-target"); }
            e.Handled = true;
        };
        panel.MouseRightButtonUp += (_, e) => { OpenEditor(t); e.Handled = true; };
        panel.MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle || !t.IsSorter) return;
            SortTargetFolderNow(t);
            CloseCloud("sort-now");
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
                e.Effects = CanAddTarget(e.Data) ? AddTargetDropEffect(e) : DragDropEffects.None;
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

        var level = CurrentLevelTargets();
        _tileDragLevel = level;
        _tileDragOriginalItems = level.ToArray();
        _tileDragOriginalDisplay = TargetStore.OrderedForDisplay(level).ToArray();
        _tileDragOriginalPositions = level.Select(target => target.TilePosition).ToArray();
        _tileReorderCommitted = false;

        var data = new DataObject();
        data.SetData(TileReorderFormat, source, false);
        try
        {
            DragDrop.DoDragDrop(sourceElement, data, DragDropEffects.Move);
        }
        finally
        {
            if (!_tileReorderCommitted) RestoreTileDragState(source);
            ClearTileDragState();
        }
    }

    private bool IsTileReorderDrag(DragEventArgs e) => e.Data.GetDataPresent(TileReorderFormat);

    private TargetItem? TileReorderSource(DragEventArgs e) =>
        e.Data.GetData(TileReorderFormat) as TargetItem;

    private void OnTileReorderPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!IsTileReorderDrag(e)) return;

        var source = TileReorderSource(e);
        if (source == null || _tileDragLevel == null || !_tileDragLevel.Contains(source)
            || !ReferenceEquals(_tileDragLevel, CurrentLevelTargets()))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var destinationIndex = TileReorderIndexAt(e, _tileDragLevel.Count);
        if (destinationIndex.HasValue
            && TargetStore.MoveTileToIndex(_tileDragLevel, source, destinationIndex.Value))
        {
            AnimateTileReorder(source);
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTileReorderPreviewDrop(object sender, DragEventArgs e)
    {
        if (!IsTileReorderDrag(e)) return;
        CommitTileReorder(e);
    }

    private int? TileReorderIndexAt(DragEventArgs e, int targetCount)
    {
        if (targetCount == 0) return null;

        var point = e.GetPosition(this);
        int offset = _currentGroup == null ? 0 : 1;
        int slotCount = targetCount + offset + 1;
        if (_cells.Length != slotCount) return null; // wheel not built for this level

        int nearestSlot = 0;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < slotCount; i++)
        {
            var slot = SlotFor(i);
            var slotDx = point.X - (slot.Left + TileLeftOffset);
            var slotDy = point.Y - (slot.Top + TileTopOffset);
            var slotDistance = slotDx * slotDx + slotDy * slotDy;
            if (slotDistance >= nearestDistance) continue;
            nearestDistance = slotDistance;
            nearestSlot = i;
        }

        if (nearestDistance > 60 * 60) return null; // pointer is not over any tile (e.g. the hub)
        if (_currentGroup != null && nearestSlot == 0) return null;
        return Math.Clamp(nearestSlot - offset, 0, targetCount - 1);
    }

    private void CommitTileReorder(DragEventArgs e)
    {
        var source = TileReorderSource(e);
        if (source == null || _tileDragLevel == null || !_tileDragLevel.Contains(source))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (TileOrderChanged())
        {
            TargetStore.Save();
            ShowToast($"Moved {source.Name}");
        }

        _tileReorderCommitted = true;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private bool TileOrderChanged() =>
        _tileDragLevel != null
        && _tileDragOriginalDisplay != null
        && !TargetStore.OrderedForDisplay(_tileDragLevel).SequenceEqual(_tileDragOriginalDisplay);

    private void RestoreTileDragState(TargetItem source)
    {
        if (_tileDragLevel == null || _tileDragOriginalItems == null || _tileDragOriginalPositions == null)
            return;

        _tileDragLevel.Clear();
        for (int i = 0; i < _tileDragOriginalItems.Length; i++)
        {
            _tileDragOriginalItems[i].TilePosition = _tileDragOriginalPositions[i];
            _tileDragLevel.Add(_tileDragOriginalItems[i]);
        }
        if (_open) AnimateTileReorder(source);
    }

    private void ClearTileDragState()
    {
        _tileDragLevel = null;
        _tileDragOriginalItems = null;
        _tileDragOriginalDisplay = null;
        _tileDragOriginalPositions = null;
        _tileReorderCommitted = false;
    }

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
        var level = CurrentLevelTargets();
        var destinationIndex = TargetStore.OrderedForDisplay(level).ToList().IndexOf(target);
        if (source != null && destinationIndex >= 0
            && TargetStore.MoveTileToIndex(level, source, destinationIndex))
        {
            AnimateTileReorder(source);
        }
        CommitTileReorder(e);
    }

    private void OnTileReorderDropToEnd(DragEventArgs e)
    {
        var source = TileReorderSource(e);
        if (source != null && TargetStore.MoveTileToEnd(CurrentLevelTargets(), source))
        {
            AnimateTileReorder(source);
        }
        CommitTileReorder(e);
    }
}
