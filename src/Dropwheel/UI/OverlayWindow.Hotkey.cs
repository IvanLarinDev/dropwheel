using System.Windows;
using System.Windows.Threading;
using Dropwheel.Services;
using WF = System.Windows.Forms;

namespace Dropwheel.UI;

public partial class OverlayWindow
{
    private HotkeyService? _hotkey;
    private DispatcherTimer? _fsTimer;
    private bool _hiddenByFullscreen;

    private void InitHotkeyAndFullscreen()
    {
        try { _hotkey = new HotkeyService(this, TargetStore.Config.Hotkey, OnHotkey); }
        catch { /* hotkey taken by another app — run without it */ }

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
            Top  = dip.Y - HalfSize;
        }
        UpdateOrbScreenPos();
        OpenCloud();
    }
}
