using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private DispatcherTimer? _idleTimer;
    private bool _dimmed;

    /// <summary>Кружок пригасает до 0.25 после N секунд без активности рядом;
    /// любое движение мыши вблизи (по данным LL-хука) возвращает прозрачность.</summary>
    private void InitIdleFade()
    {
        int s = TargetStore.Config.IdleFadeSeconds;
        if (s <= 0) return;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(s) };
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer!.Stop();
            if (!_open) { _dimmed = true; Orb.Opacity = 0.25; }
        };
        _idleTimer.Start();
    }

    private void WakeIdle(double distSq)
    {
        if (_idleTimer == null || distSq > _closeR2) return;
        if (_dimmed) { _dimmed = false; Orb.Opacity = TargetStore.Config.OrbOpacity; }
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    /// <summary>Применить изменённые настройки без перезапуска.</summary>
    public void ApplySettings()
    {
        Orb.Opacity = TargetStore.Config.OrbOpacity;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(TargetStore.Config.HoverDelayMs);
        _dimmed = false;
        _idleTimer?.Stop();
        _idleTimer = null;
        InitIdleFade();
        _hotkey?.Dispose();
        _hotkey = null;
        try { _hotkey = new HotkeyService(this, TargetStore.Config.Hotkey, OnHotkey); }
        catch { /* хоткей занят */ }
    }

    public void OpenSettings()
    {
        new SettingsWindow { Owner = this }.ShowDialog();
    }
}
