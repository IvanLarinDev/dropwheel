using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private HotkeyService? _hotkey;
    private string _hotkeyActive = ""; // the last successfully registered combo
    private HotkeyService? _hotkeyAtOrb;
    private const int HotkeyAtOrbId = 0x0D29; // distinct from the primary (0x0D27) and trial (0x0D28) ids
    private DispatcherTimer? _fsTimer;
    private bool _hiddenByFullscreen;

    private void InitHotkeyAndFullscreen()
    {
        // At startup there is no previous working combo yet: if the config is taken by another
        // app, just run without a hotkey and quietly leave a trace in the log.
        ApplyHotkey(TargetStore.Config.Hotkey, notify: false);
        ApplyHotkeyAtOrb(TargetStore.Config.HotkeyAtOrb, notify: false);

        _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fsTimer.Tick += (_, _) =>
        {
            bool fs = FullscreenDetector.IsFullscreenActive();
            if (fs && !_hiddenByFullscreen) { _hiddenByFullscreen = true; CloseCloud(); Hide(); }
            else if (!fs && _hiddenByFullscreen)
            {
                _hiddenByFullscreen = false;
                Show();
                ShowFullscreenReturnHint();
            }
        };
        _fsTimer.Start();

        Closed += (_, _) => { _hotkey?.Dispose(); _hotkeyAtOrb?.Dispose(); _fsTimer?.Stop(); };
    }

    /// <summary>Hotkey: open the wheel at the cursor; press again to close and send the orb home.</summary>
    private void OnHotkey()
    {
        if (!IsVisible) return; // hidden by fullscreen
        if (_open)
        {
            CloseCloud();
            PlaceWindow(); // back home
            return;
        }
        if (!TryCursorDip(out var dip)) return; // no source yet — leave the window where it is
        PlaceWindowAtCenter(dip.X, dip.Y);
        UpdateOrbScreenPos();
        OpenCloud();
    }

    /// <summary>Second hotkey: open the wheel at the orb's home position, not at the cursor; press again
    /// to close. The mouse is never moved, so the wheel appears where the orb lives.</summary>
    private void OnHotkeyAtOrb()
    {
        if (!IsVisible) return; // hidden by fullscreen
        if (_open) { CloseCloud(); return; }
        PlaceWindow(); // the orb's home position
        UpdateOrbScreenPos();
        OpenCloud();
    }

    /// <summary>Registers (or clears) the optional "open at orb" hotkey under its own id. Empty unregisters
    /// it. A combo taken by another app is logged and, when notify is set, reported — the primary hotkey is
    /// unaffected either way.</summary>
    private void ApplyHotkeyAtOrb(string hotkey, bool notify)
    {
        _hotkeyAtOrb?.Dispose();
        _hotkeyAtOrb = null;
        if (string.IsNullOrWhiteSpace(hotkey)) return;
        try
        {
            _hotkeyAtOrb = new HotkeyService(this, hotkey, OnHotkeyAtOrb, HotkeyAtOrbId);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to register 'open at orb' hotkey '{hotkey}'", ex);
            if (notify) ShowToast($"Hotkey {hotkey} is taken by another app", kind: ToastKind.Warning);
        }
    }

    /// <summary>Registers the hotkey from config. If the combo is taken by another process, it falls
    /// back to the last working combo and rolls the config back — so a wrong entry in settings doesn't
    /// leave the app with no hotkey at all. First release the old registration (otherwise the same id
    /// is in use), then try the new one; on failure re-register the previous one.</summary>
    private void ApplyHotkey(string hotkey, bool notify)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        try
        {
            _hotkey = new HotkeyService(this, hotkey, OnHotkey);
            _hotkeyActive = hotkey;
            return;
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to register hotkey '{hotkey}'", ex);
        }

        bool rolledBack = _hotkeyActive.Length > 0 && _hotkeyActive != hotkey;
        if (rolledBack)
        {
            TargetStore.Config.Hotkey = _hotkeyActive;
            TrySaveConfig(); // runs at startup — a write failure here must not crash init

            try { _hotkey = new HotkeyService(this, _hotkeyActive, OnHotkey); }
            catch (Exception ex) { ErrorLog.Write($"Failed to restore previous hotkey '{_hotkeyActive}'", ex); }
        }
        if (notify)
            ShowToast(rolledBack
                ? $"Hotkey {hotkey} is taken — kept the previous {_hotkeyActive}"
                : $"Hotkey {hotkey} is taken by another app", kind: ToastKind.Warning);
    }
}
