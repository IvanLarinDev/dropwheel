using System.Windows;
using System.Windows.Threading;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private HotkeyService? _hotkey;
    private string _hotkeyActive = ""; // the last successfully registered combo
    private DispatcherTimer? _fsTimer;
    private bool _hiddenByFullscreen;

    private void InitHotkeyAndFullscreen()
    {
        // At startup there is no previous working combo yet: if the config is taken by another
        // app, just run without a hotkey and quietly leave a trace in the log.
        ApplyHotkey(TargetStore.Config.Hotkey, notify: false);

        _fsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fsTimer.Tick += (_, _) =>
        {
            bool fs = FullscreenDetector.IsFullscreenActive();
            if (fs && !_hiddenByFullscreen) { _hiddenByFullscreen = true; CloseCloud(); Hide(); }
            else if (!fs && _hiddenByFullscreen) { _hiddenByFullscreen = false; Show(); }
        };
        _fsTimer.Start();

        Closed += (_, _) => { _hotkey?.Dispose(); _fsTimer?.Stop(); };
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
        var c = WF.Cursor.Position; // device px
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } ct)
        {
            var dip = ct.TransformFromDevice.Transform(new Point(c.X, c.Y));
            Left = dip.X - HalfSize;
            Top = dip.Y - HalfSize;
        }
        UpdateOrbScreenPos();
        OpenCloud();
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
            TargetStore.Save();
            try { _hotkey = new HotkeyService(this, _hotkeyActive, OnHotkey); }
            catch (Exception ex) { ErrorLog.Write($"Failed to restore previous hotkey '{_hotkeyActive}'", ex); }
        }
        if (notify)
            ShowToast(rolledBack
                ? $"Hotkey {hotkey} is taken — kept the previous {_hotkeyActive}"
                : $"Hotkey {hotkey} is taken by another app");
    }
}
