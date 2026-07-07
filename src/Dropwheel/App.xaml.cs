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

        // Страховочная сетка: приложение живёт в трее, и падение в обработчике
        // drop/click/таймера не должно ронять весь процесс. Ошибку логируем,
        // а не глотаем молча, и показываем короткий тост.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write("Необработанное исключение", args.Exception);
            _overlay?.NotifyError("Что-то пошло не так — подробности в error.log");
            args.Handled = true;
        };

        StartupService.RefreshPath();
        TargetStore.Load();
        _overlay = new OverlayWindow();
        _overlay.Show();
        InitTray();
    }
}
