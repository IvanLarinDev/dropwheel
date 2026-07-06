using Dropwheel.Services;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

public partial class App
{
    private void InitTray()
    {
        _tray = new WF.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Dropwheel"
        };
        var menu = new WF.ContextMenuStrip();
        var auto = new WF.ToolStripMenuItem("Start with Windows")
        { Checked = StartupService.IsEnabled, CheckOnClick = true };
        auto.Click += (_, _) => StartupService.SetEnabled(auto.Checked);
        menu.Items.Add(auto);
        menu.Items.Add("Settings…", null, (_, _) => _overlay?.OpenSettings());
        menu.Items.Add("Open config folder", null, (_, _) => LaunchService.OpenConfigFolder());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _overlay?.ToggleCloud();
    }

    private static SD.Icon LoadAppIcon()
    {
        // Preferred: the branded .ico embedded as a WPF resource. Reliable regardless of how
        // the app is launched, and Icon(stream, size) picks the frame matching the tray size.
        try
        {
            var uri = new Uri("pack://application:,,,/dropwheel.ico");
            if (System.Windows.Application.GetResourceStream(uri) is { } res)
            {
                using var s = res.Stream;
                return new SD.Icon(s, WF.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        // Fallback: the running exe's associated icon (works for exe/published builds).
        try
        {
            if (Environment.ProcessPath is { } p && SD.Icon.ExtractAssociatedIcon(p) is { } i)
                return i;
        }
        catch { }
        return SD.SystemIcons.Application;
    }

    private void ExitApp()
    {
        // No Save() here on purpose: config is written on every change,
        // and saving on exit let a stale instance overwrite edits made
        // on disk or by another instance.
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }
}
