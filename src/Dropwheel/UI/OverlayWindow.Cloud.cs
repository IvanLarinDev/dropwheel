using System.Windows.Controls;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
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
        Cloud.Children.Clear();
    }

    /// <summary>Радиальная раскладка: закреплённые ближе к центру,
    /// первые 6 — внутреннее кольцо (r=95), остальные — внешнее (r=170).</summary>
    private void BuildCloud()
    {
        Cloud.Children.Clear();
        var items = TargetStore.Config.Targets.OrderByDescending(t => t.Pinned).ToList();
        int ring1 = Math.Min(6, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            bool inner = i < ring1;
            int idx    = inner ? i : i - ring1;
            int count  = inner ? ring1 : items.Count - ring1;
            double r   = inner ? 95 : 170;
            double a   = -Math.PI / 2 + idx * 2 * Math.PI / Math.Max(count, 1) + (inner ? 0 : 0.26);
            var bubble = MakeBubble(items[i]);
            Canvas.SetLeft(bubble, HalfSize + r * Math.Cos(a) - 35);
            Canvas.SetTop(bubble,  HalfSize + r * Math.Sin(a) - 38);
            Cloud.Children.Add(bubble);
        }
    }
}
