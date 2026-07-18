using System.Windows;
using Dropwheel.Services;
using Dropwheel.UI;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Process-lifetime singleton: owned resources are released by the one-shot shutdown coordinator; a WPF Application is not idiomatically IDisposable.")]
public partial class App : Application
{
    private static readonly TimeSpan ExplorerBridgeShutdownTimeout = TimeSpan.FromSeconds(2);
    private static Mutex? _mutex;
    private readonly ShutdownCoordinator _shutdownCoordinator = new();
    private bool _ownsMutex;
    private bool _exitAfterExplorerDelivery;
    private string? _smokeProbePath;
    private WF.NotifyIcon? _tray;
    private OverlayWindow? _overlay;
    private WatcherService? _watcher;
    private CancellationTokenSource? _explorerBridgeCts;
    private Task? _explorerBridgeTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        var command = ExplorerBridgeCommand.Parse(e.Args);
        if (command.Kind == ExplorerBridgeCommandKind.Invalid)
        {
            Shutdown(2);
            return;
        }
        if (command.Kind is ExplorerBridgeCommandKind.SmokeTest or ExplorerBridgeCommandKind.SmokeSendFiles)
        {
            TargetStore.DirOverride = command.SmokeProfileRoot
                ?? throw new InvalidOperationException("Smoke profile root is required.");
            _smokeProbePath = command.SmokeProbePath
                ?? throw new InvalidOperationException("Smoke probe path is required.");
        }
        _exitAfterExplorerDelivery = command.Kind == ExplorerBridgeCommandKind.SmokeTest;
        if (command.Kind == ExplorerBridgeCommandKind.SmokeSendFiles)
        {
            Shutdown(ExplorerBridgeIpc.TrySendFiles(command.Paths) ? 0 : 3);
            return;
        }
        if (HandleExplorerBridgeUtilityCommand(command))
        {
            Shutdown();
            return;
        }

        var mutex = new Mutex(true, "Dropwheel_SingleInstance", out bool isNew);
        _mutex = mutex;
        if (!isNew)
        {
            _mutex = null;
            mutex.Dispose();
            if (command.Kind == ExplorerBridgeCommandKind.SendToFiles)
                ExplorerBridgeIpc.TrySendFiles(command.Paths);
            Shutdown();
            return;
        }
        _ownsMutex = true;
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write("Unhandled exception", args.Exception);
            if (AppFailurePolicy.MustTerminate(args.Exception))
            {
                args.Handled = true;
                try
                {
                    MessageBox.Show(
                        "Dropwheel encountered an unexpected error and will close.\n\nSee error.log for details.",
                        "Dropwheel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (Exception notificationError)
                {
                    ErrorLog.Write("Could not show the fatal error message", notificationError);
                }
                _ = RequestShutdownAsync();
            }
        };

        // A failure here happens before the overlay and theme exist, so there is no toast or themed
        // dialog to show it in — fall back to the system message box and exit cleanly instead of a
        // silent crash on, say, an unreadable config.
        try
        {
            if (!_exitAfterExplorerDelivery)
                StartupService.RefreshPath();
            TargetStore.Load();
            _overlay = new OverlayWindow();
            _overlay.Show();
            InitTray(maintainSystemIntegrations: !_exitAfterExplorerDelivery);
            StartExplorerBridgeServer();
            if (command.Kind == ExplorerBridgeCommandKind.SendToFiles)
                Dispatcher.BeginInvoke(() => _overlay.OpenFromExplorerFiles(command.Paths));

            _watcher = new WatcherService(Dispatcher, ShowSortedToast);
            _watcher.Start();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup failed", ex);
            MessageBox.Show(
                "Dropwheel couldn't start.\n\n" + ex.Message + "\n\nSee error.log for details.",
                "Dropwheel", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = RequestShutdownAsync();
        }
    }

    /// <summary>Unobtrusively reports that background auto-sort moved something. The toast coalesces
    /// a burst of files into one notification (see WatcherService).</summary>
    private void ShowSortedToast(int count) =>
        _tray?.ShowBalloonTip(3000, "Dropwheel", $"Sorted {count} file(s)", WF.ToolTipIcon.Info);

    private static bool HandleExplorerBridgeUtilityCommand(ExplorerBridgeCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case ExplorerBridgeCommandKind.InstallSendTo:
                    ExplorerBridgeService.InstallSendTo(command.AppPath ?? CurrentAppPath());
                    MessageBox.Show(
                        "Explorer SendTo shortcut installed.",
                        "Dropwheel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return true;
                case ExplorerBridgeCommandKind.UninstallSendTo:
                    ExplorerBridgeService.UninstallSendTo();
                    MessageBox.Show(
                        "Explorer SendTo shortcut removed.",
                        "Dropwheel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Explorer bridge utility command failed", ex);
            MessageBox.Show(
                "Explorer bridge command failed.\n\n" + ex.Message,
                "Dropwheel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return true;
        }
    }

    // Assembly.Location is NOT a valid fallback here: in the single-file publish it returns an
    // empty string, which would silently produce a broken SendTo shortcut. ProcessPath is present
    // on Windows in practice; if it ever is not, failing loudly beats installing a dead shortcut.
    private static string CurrentAppPath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the executable path.");

    private void StartExplorerBridgeServer()
    {
        if (_overlay == null) return;
        _explorerBridgeCts = new CancellationTokenSource();
        _explorerBridgeTask = ExplorerBridgeIpc.RunServerAsync(
            paths => Dispatcher.BeginInvoke(() =>
            {
                if (_exitAfterExplorerDelivery)
                {
                    var isExpectedProbe = SmokeTestProtocol.IsExpectedProbe(paths, _smokeProbePath!);
                    if (isExpectedProbe)
                    {
                        SmokeTestProtocol.WriteAcknowledgement(TargetStore.Dir, _smokeProbePath!);
                    }
                    SmokeTestProtocol.WriteDeliveryMarker(TargetStore.Dir);
                    if (isExpectedProbe)
                    {
                        _ = RequestShutdownAsync();
                    }
                }
                else
                    _overlay.OpenFromExplorerFiles(paths);
            }),
            _explorerBridgeCts.Token);
    }

    private Task RequestShutdownAsync() =>
        _shutdownCoordinator.RequestAsync(ShutdownCoreAsync);

    private async Task ShutdownCoreAsync()
    {
        var watcher = Interlocked.Exchange(ref _watcher, null);
        TryCleanup("Could not stop the folder watcher", () => watcher?.Stop());

        var cancellation = Interlocked.Exchange(ref _explorerBridgeCts, null);
        var bridgeTask = Interlocked.Exchange(ref _explorerBridgeTask, null);
        await BackgroundTaskShutdown.CancelAndWaitAsync(
            cancellation,
            bridgeTask,
            ExplorerBridgeShutdownTimeout,
            error => ErrorLog.Write(
                error is TimeoutException
                    ? "Explorer bridge server did not stop within two seconds"
                    : "Explorer bridge server stopped with an error",
                error));
        TryCleanup("Could not dispose the Explorer bridge cancellation source", () => cancellation?.Dispose());

        var tray = Interlocked.Exchange(ref _tray, null);
        TryCleanup("Could not dispose the tray icon", () =>
        {
            if (tray is null) return;
            tray.Visible = false;
            tray.Dispose();
        });
        var appIcon = Interlocked.Exchange(ref _appIcon, null);
        TryCleanup("Could not dispose the application icon", () => appIcon?.Dispose());

        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (_ownsMutex)
        {
            _ownsMutex = false;
            TryCleanup("Could not release the single-instance mutex", () => mutex?.ReleaseMutex());
        }
        TryCleanup("Could not dispose the single-instance mutex", () => mutex?.Dispose());
        TryCleanup("Could not shut down the application", Shutdown);
    }

    private static void TryCleanup(string context, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(context, ex);
        }
    }
}
