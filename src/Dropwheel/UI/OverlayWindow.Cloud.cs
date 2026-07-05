using System.Windows.Controls;
using System.Windows.Threading;
using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private TargetItem? _currentGroup;   // null = корневой уровень
    private TargetItem? _pendingGroup;   // группа, открываемая drag-hover'ом
    private bool _pendingBack;
    private DispatcherTimer? _groupHover;

    private void OpenCloud()
    {
        if (_open) return;
        _open = true;
        BuildCloud();
    }

    private void CloseCloud()
    {
        if (!_open) return;
        _open = false;
        _currentGroup = null;
        _groupHover?.Stop();
        Cloud.Children.Clear();
    }

    /// <summary>Радиальная раскладка текущего уровня: закреплённые ближе к центру,
    /// первые 6 — внутреннее кольцо (r=95), остальные — внешнее (r=170).
    /// Внутри группы первым идёт бабл «назад».</summary>
    private void BuildCloud()
    {
        Cloud.Children.Clear();
        var source = _currentGroup?.Children ?? TargetStore.Config.Targets;
        var bubbles = new List<System.Windows.FrameworkElement>();
        if (_currentGroup != null) bubbles.Add(MakeBackBubble());
        bubbles.AddRange(source.OrderByDescending(t => t.Pinned).Select(MakeBubble));

        int ring1 = Math.Min(6, bubbles.Count);
        for (int i = 0; i < bubbles.Count; i++)
        {
            bool inner = i < ring1;
            int idx    = inner ? i : i - ring1;
            int count  = inner ? ring1 : bubbles.Count - ring1;
            double r   = inner ? 95 : 170;
            double a   = -Math.PI / 2 + idx * 2 * Math.PI / Math.Max(count, 1) + (inner ? 0 : 0.26);
            Canvas.SetLeft(bubbles[i], HalfSize + r * Math.Cos(a) - 35);
            Canvas.SetTop(bubbles[i],  HalfSize + r * Math.Sin(a) - 38);
            Cloud.Children.Add(bubbles[i]);
        }
    }

    private void EnterGroup(TargetItem? group)
    {
        _currentGroup = group;
        if (_open) BuildCloud();
    }

    private void StartGroupHover(TargetItem? group, bool back)
    {
        if (_groupHover == null)
        {
            _groupHover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _groupHover.Tick += (_, _) =>
            {
                _groupHover!.Stop();
                EnterGroup(_pendingBack ? null : _pendingGroup);
            };
        }
        _pendingGroup = group; _pendingBack = back;
        if (!_groupHover.IsEnabled) _groupHover.Start();
    }
}
