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
            Icon = SD.SystemIcons.Application,
            Visible = true,
            Text = "Dropwheel"
        };
        var menu = new WF.ContextMenuStrip();
        var auto = new WF.ToolStripMenuItem("Start with Windows")
        { Checked = StartupService.IsEnabled, CheckOnClick = true };
        auto.Click += (_, _) => StartupService.SetEnabled(auto.Checked);
        menu.Items.Add(auto);
        menu.Items.Add("Open config folder", null, (_, _) => LaunchService.OpenConfigFolder());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _overlay?.ToggleCloud();
    }

    private void ExitApp()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        TargetStore.Save();
        Shutdown();
    }
}
