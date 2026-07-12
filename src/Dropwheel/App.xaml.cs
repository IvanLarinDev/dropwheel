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
    private WatcherService? _watcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Dropwheel_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }
        base.OnStartup(e);

        // Safety net: the app lives in the tray, and a crash in a drop/click/timer handler must
        // not take down the whole process. Log the error instead of swallowing it, and show a
        // short toast.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write("Unhandled exception", args.Exception);
            _overlay?.NotifyError("Something went wrong — see error.log for details");
            args.Handled = true;
        };

        // A failure here happens before the overlay and theme exist, so there is no toast or themed
        // dialog to show it in — fall back to the system message box and exit cleanly instead of a
        // silent crash on, say, an unreadable config.
        try
        {
            StartupService.RefreshPath();
            TargetStore.Load();
            _overlay = new OverlayWindow();
            _overlay.Show();
            InitTray();

            _watcher = new WatcherService(Dispatcher, ShowSortedToast);
            _watcher.Start();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup failed", ex);
            MessageBox.Show(
                "Dropwheel couldn't start.\n\n" + ex.Message + "\n\nSee error.log for details.",
                "Dropwheel", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>Unobtrusively reports that background auto-sort moved something. The toast coalesces
    /// a burst of files into one notification (see WatcherService).</summary>
    private void ShowSortedToast(int count) =>
        _tray?.ShowBalloonTip(3000, "Dropwheel", $"Sorted {count} file(s)", WF.ToolTipIcon.Info);
}
