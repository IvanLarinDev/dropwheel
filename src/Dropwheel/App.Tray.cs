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
        StyleTrayMenu(menu);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _overlay?.ToggleCloud();
    }

    /// <summary>WinForms tray menu ignores the WPF theme, so paint it from the palette by hand for
    /// the dark themes. Applied once at startup; a theme change takes effect on next launch.</summary>
    private static void StyleTrayMenu(WF.ContextMenuStrip menu)
    {
        var p = Dropwheel.UI.Palettes.Current;
        if (!p.Dark) return;
        menu.RenderMode = WF.ToolStripRenderMode.Professional;
        menu.Renderer = new WF.ToolStripProfessionalRenderer(new DarkMenuColors(p)) { RoundedEdges = false };
        menu.BackColor = ToSd(p.Surface);
        menu.ForeColor = ToSd(p.Text);
    }

    private static SD.Color ToSd(System.Windows.Media.Color c) => SD.Color.FromArgb(c.R, c.G, c.B);

    private static SD.Color Blend(System.Windows.Media.Color a, System.Windows.Media.Color b, double t) =>
        SD.Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));

    private sealed class DarkMenuColors : WF.ProfessionalColorTable
    {
        private readonly SD.Color _bg, _hover, _line;

        public DarkMenuColors(Dropwheel.UI.Palette p)
        {
            _bg = ToSd(p.Surface);
            _hover = Blend(p.Surface, p.Accent, 0.30);
            _line = ToSd(p.Border);
        }

        public override SD.Color ToolStripDropDownBackground => _bg;
        public override SD.Color ImageMarginGradientBegin => _bg;
        public override SD.Color ImageMarginGradientMiddle => _bg;
        public override SD.Color ImageMarginGradientEnd => _bg;
        public override SD.Color MenuBorder => _line;
        public override SD.Color MenuItemBorder => _line;
        public override SD.Color MenuItemSelected => _hover;
        public override SD.Color MenuItemSelectedGradientBegin => _hover;
        public override SD.Color MenuItemSelectedGradientEnd => _hover;
        public override SD.Color CheckBackground => _hover;
        public override SD.Color CheckSelectedBackground => _hover;
        public override SD.Color SeparatorDark => _line;
        public override SD.Color SeparatorLight => _bg;
    }

    // Собственная иконка трея (её нативный хендл нужно освободить при выходе). Остаётся null,
    // если используется общесистемная SystemIcons.Application — её трогать Dispose нельзя.
    private SD.Icon? _appIcon;

    private SD.Icon LoadAppIcon()
    {
        // Preferred: the branded .ico embedded as a WPF resource. Reliable regardless of how
        // the app is launched, and Icon(stream, size) picks the frame matching the tray size.
        try
        {
            var uri = new Uri("pack://application:,,,/dropwheel.ico");
            if (System.Windows.Application.GetResourceStream(uri) is { } res)
            {
                using var s = res.Stream;
                return _appIcon = new SD.Icon(s, WF.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        // Fallback: the running exe's associated icon (works for exe/published builds).
        try
        {
            if (Environment.ProcessPath is { } p && SD.Icon.ExtractAssociatedIcon(p) is { } i)
                return _appIcon = i;
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
        _appIcon?.Dispose(); // NotifyIcon.Dispose не освобождает переданную ему иконку
        Shutdown();
    }
}
