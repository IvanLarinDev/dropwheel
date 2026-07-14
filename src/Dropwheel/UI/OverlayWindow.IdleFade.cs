using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private DispatcherTimer? _idleTimer;
    private bool _dimmed;
    private long _lastWake; // throttles idle-timer restarts on rapid in-zone mouse moves

    /// <summary>The orb dims to 0.25 after N seconds without nearby activity;
    /// any mouse movement close by (via the LL hook) restores opacity.</summary>
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

    /// <summary>Restores the orb to its configured opacity if the idle fade had dimmed it.</summary>
    private void RestoreOrbOpacity()
    {
        if (_dimmed) { _dimmed = false; Orb.Opacity = TargetStore.Config.OrbOpacity; }
    }

    private void WakeIdle(double distSq)
    {
        if (_idleTimer == null || distSq > _closeR2) return;
        RestoreOrbOpacity();
        // The fade is seconds-scale, so restarting the timer on every in-zone mouse move is wasted churn;
        // coalesce restarts to a few times a second while keeping the same "activity resets the fade".
        long now = Environment.TickCount64;
        if (now - _lastWake < 200) return;
        _lastWake = now;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    /// <summary>Apply changed settings without a restart.</summary>
    public void ApplySettings()
    {
        if (Orb.ContextMenu is System.Windows.Controls.ContextMenu m) Themes.ApplyMenu(m);
        Themes.RaiseThemeChanged();
        PaintHub();
        ApplyModeWindow(); // the overflow mode may have changed → resize the fixed window (wheel is closed here)
        if (_open) BuildCloud();
        Orb.Opacity = TargetStore.Config.OrbOpacity;
        _hoverTimer.Interval = HoverInterval();
        _dimmed = false;
        _idleTimer?.Stop();
        _idleTimer = null;
        InitIdleFade();
        ApplyGroupShortcutSettings();
        // The string was already validated for parsing in settings, but being taken by another
        // process is only checked here: on failure the previous working combo stays.
        ApplyHotkey(TargetStore.Config.Hotkey, notify: true);
        ApplyHotkeyAtOrb(TargetStore.Config.HotkeyAtOrb, notify: true);
    }

    public void OpenSettings()
    {
        ResetGroupShortcutInput(preserveActivation: false);
        new SettingsWindow { Owner = this }.ShowDialog();
    }
}
