using System.Windows;
using Dropwheel.Services;
using Dropwheel.UI;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

public partial class App : Application
{
    private static Mutex? _mutex;
    private WF.NotifyIcon? _tray;
    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Dropwheel_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }
        base.OnStartup(e);

        StartupService.RefreshPath();
        TargetStore.Load();
        _overlay = new OverlayWindow();
        _overlay.Show();
        InitTray();
    }
}
