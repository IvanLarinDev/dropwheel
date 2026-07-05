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
        // Save() здесь намеренно нет: конфиг пишется в момент каждого изменения,
        // а сохранение при выходе позволяло устаревшему экземпляру затирать
        // правки, сделанные на диске или другим экземпляром.
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }
}
