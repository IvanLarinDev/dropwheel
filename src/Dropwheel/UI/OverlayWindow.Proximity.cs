using System.Windows;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private bool _proximityOpened, _movingOrb;
    private double _orbSX, _orbSY, _openR2, _closeR2;

    private void InitProximity()
    {
        UpdateOrbScreenPos();
        MouseHook.MouseMoved += OnGlobalMouse;
        Closed += (_, _) => MouseHook.Stop();
        MouseHook.Start(); // хук ставится из UI-потока → колбэк тоже в нём
    }

    private void UpdateOrbScreenPos()
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } ct }) return;
        var p = Orb.PointToScreen(new Point(23, 23)); // центр кружка, device px
        _orbSX = p.X; _orbSY = p.Y;
        double m = ct.TransformToDevice.M11;
        _openR2  = 150 * m * 150 * m;  // радиус раскрытия
        _closeR2 = 340 * m * 340 * m;  // радиус сворачивания
    }

    /// <summary>Зажатая ЛКМ + курсор рядом с кружком = вероятный drag →
    /// раскрываем колесо заранее, до входа drag в наше окно.</summary>
    private void OnGlobalMouse(int x, int y, bool leftDown)
    {
        if (_movingOrb) return;
        double dx = x - _orbSX, dy = y - _orbSY, d2 = dx * dx + dy * dy;
        WakeIdle(d2);
        if (!leftDown) { _proximityOpened = false; return; }
        if (!_open && d2 < _openR2)
        { _proximityOpened = true; OpenCloud(); }
        else if (_open && _proximityOpened && d2 > _closeR2)
        { _proximityOpened = false; CloseCloud(); }
    }
}
