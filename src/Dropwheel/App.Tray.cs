using System.Diagnostics;
using Dropwheel.Services;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

public partial class App
{
    // Runtime-only: auto-sort is paused from the tray without touching each target's Watch flag, so a
    // restart resumes it. Watched folders keep accumulating while paused and are swept on resume.
    private bool _watchPaused;

    private void InitTray()
    {
        _tray = new WF.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Dropwheel"
        };
        var menu = new WF.ContextMenuStrip();
        // Shown only while a fullscreen app has hidden the orb: the tray is the one place the user can
        // reach then, so it explains where the orb went instead of leaving them to think it crashed.
        var fsStatus = new WF.ToolStripMenuItem("Orb hidden — fullscreen app active")
        { Enabled = false, Visible = false };
        menu.Items.Add(fsStatus);
        var header = new WF.ToolStripMenuItem($"Dropwheel {AppVersion()}") { Enabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new WF.ToolStripSeparator());
        var auto = new WF.ToolStripMenuItem("Start with Windows")
        { Checked = StartupService.IsEnabled, CheckOnClick = true };
        auto.Click += (_, _) =>
        {
            try { StartupService.SetEnabled(auto.Checked); }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not change the 'start with Windows' setting", ex);
                auto.Checked = !auto.Checked; // undo the toggle CheckOnClick already applied
                _tray?.ShowBalloonTip(4000, "Dropwheel",
                    "Couldn't change the 'start with Windows' setting.", WF.ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(auto);
        try
        {
            if (ExplorerBridgeService.NeedsSendToUpgrade())
                ExplorerBridgeService.InstallSendTo(CurrentAppPath());
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not upgrade the Explorer SendTo shortcut", ex);
        }
        var sendTo = new WF.ToolStripMenuItem("Explorer SendTo shortcut")
        { Checked = ExplorerBridgeService.IsSendToInstalled(), CheckOnClick = true };
        sendTo.Click += (_, _) =>
        {
            try
            {
                if (sendTo.Checked) ExplorerBridgeService.InstallSendTo(CurrentAppPath());
                else ExplorerBridgeService.UninstallSendTo();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not change the Explorer SendTo shortcut", ex);
                sendTo.Checked = !sendTo.Checked;
                _tray?.ShowBalloonTip(4000, "Dropwheel",
                    "Couldn't change the Explorer SendTo shortcut.", WF.ToolTipIcon.Warning);
            }
        };
        menu.Items.Add(sendTo);
        var pauseSort = new WF.ToolStripMenuItem("Pause auto-sort")
        {
            Checked = _watchPaused,
            CheckOnClick = true,
            ToolTipText = "Temporarily stop watched folders from auto-sorting. Files pile up while paused "
                        + "and are sorted when resumed. Resets on restart; the Watch setting is untouched.",
        };
        pauseSort.Click += (_, _) =>
        {
            _watchPaused = pauseSort.Checked;
            if (_watchPaused) _watcher?.Stop();
            else _watcher?.Start();
        };
        menu.Items.Add(pauseSort);
        menu.Items.Add("Settings…", null, (_, _) => _overlay?.OpenSettings());
        menu.Items.Add("Open config folder", null, (_, _) => LaunchService.OpenConfigFolder());
        var recentDrops = new WF.ToolStripMenuItem("Recent drops");
        menu.Items.Add(recentDrops);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        StyleTrayMenu(menu);
        menu.Opening += (_, _) =>
        {
            fsStatus.Visible = Dropwheel.Services.FullscreenDetector.IsFullscreenActive();
            sendTo.Checked = ExplorerBridgeService.IsSendToInstalled();
            PopulateRecentDrops(recentDrops);
            StyleTrayMenu(menu);
        };
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _overlay?.ToggleCloud();
    }

    private void PopulateRecentDrops(WF.ToolStripMenuItem recentDrops)
    {
        recentDrops.DropDownItems.Clear();
        recentDrops.DropDown.ShowItemToolTips = true;
        var entries = DropHistoryService.LoadForMenu();
        if (entries.Count == 0)
        {
            recentDrops.DropDownItems.Add(new WF.ToolStripMenuItem("No drops yet")
            { Enabled = false, ToolTipText = "Drop actions will appear here." });
        }
        else
        {
            foreach (var entry in entries)
            {
                var location = DropHistoryService.DestinationLocation(entry);
                var item = new WF.ToolStripMenuItem(DropHistoryService.MenuSummary(entry))
                {
                    Enabled = location != null,
                    ToolTipText = DropHistoryService.MenuToolTip(entry),
                };
                if (location != null)
                    item.Click += (_, _) => OpenDropHistoryLocation(location.Value);
                recentDrops.DropDownItems.Add(item);
            }
            recentDrops.DropDownItems.Add(new WF.ToolStripSeparator());
            recentDrops.DropDownItems.Add("Copy list", null, (_, _) => CopyDropHistoryList());
            recentDrops.DropDownItems.Add("Clear history...", null, (_, _) => ClearDropHistory(recentDrops));
        }
        recentDrops.DropDownItems.Add("Open history file...", null, (_, _) => OpenDropHistoryFile());
    }

    /// <summary>Puts the recent-drops list on the clipboard as plain text. The clipboard occasionally
    /// refuses to open when another app holds it, so one quick retry before giving up with a balloon.</summary>
    private void CopyDropHistoryList()
    {
        var text = DropHistoryService.ClipboardText(DropHistoryService.LoadForMenu());
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                if (attempt >= 1)
                {
                    ErrorLog.Write("Could not copy the drop history to the clipboard", ex);
                    _tray?.ShowBalloonTip(4000, "Dropwheel",
                        "Couldn't copy the list — the clipboard is busy.", WF.ToolTipIcon.Warning);
                    return;
                }
                System.Threading.Thread.Sleep(50);
            }
        }
    }

    private void ClearDropHistory(WF.ToolStripMenuItem recentDrops)
    {
        var result = WF.MessageBox.Show(
            "Clear recent drop history?",
            "Dropwheel",
            WF.MessageBoxButtons.YesNo,
            WF.MessageBoxIcon.Question);
        if (result != WF.DialogResult.Yes) return;

        try
        {
            DropHistoryService.Clear();
            PopulateRecentDrops(recentDrops);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not clear drop history", ex);
            _tray?.ShowBalloonTip(4000, "Dropwheel",
                "Couldn't clear drop history.", WF.ToolTipIcon.Warning);
        }
    }

    private void OpenDropHistoryLocation(DropHistoryLocation location)
    {
        try
        {
            var startInfo = location.SelectFile
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{location.Path}\"")
                : new ProcessStartInfo(location.Path);
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Could not open drop destination '{location.Path}'", ex);
            _tray?.ShowBalloonTip(4000, "Dropwheel",
                "Couldn't open the drop destination.", WF.ToolTipIcon.Warning);
        }
    }

    private void OpenDropHistoryFile()
    {
        try
        {
            DropHistoryService.EnsureFileExists();
            Process.Start(new ProcessStartInfo(DropHistoryService.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not open drop history", ex);
            _tray?.ShowBalloonTip(4000, "Dropwheel",
                "Couldn't open drop history.", WF.ToolTipIcon.Warning);
        }
    }

    /// <summary>WinForms tray menu ignores the WPF theme, so paint it from the palette by hand.
    /// Re-applied on each open so a theme change takes effect live: dark themes get the custom
    /// renderer, light themes fall back to the default one.</summary>
    private static void StyleTrayMenu(WF.ContextMenuStrip menu)
    {
        var p = Dropwheel.UI.Palettes.Current;
        if (!p.Dark)
        {
            menu.RenderMode = WF.ToolStripRenderMode.ManagerRenderMode;
            menu.BackColor = SD.SystemColors.Menu;
            menu.ForeColor = SD.SystemColors.MenuText;
            return;
        }
        menu.RenderMode = WF.ToolStripRenderMode.Professional;
        menu.Renderer = new WF.ToolStripProfessionalRenderer(new DarkMenuColors(p)) { RoundedEdges = false };
        menu.BackColor = ToSd(p.Surface);
        menu.ForeColor = ToSd(p.Text);
    }

    /// <summary>The app version for the tray header, from the assembly's informational version
    /// (without the trailing +commit), falling back to the plain assembly version.</summary>
    private static string AppVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var attr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
            asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        var info = attr?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return "v" + info.Split('+')[0];
        return "v" + (asm.GetName().Version?.ToString() ?? "?");
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

    // The tray's own icon (its native handle must be freed on exit). Stays null when the shared
    // SystemIcons.Application is used — that one must not be disposed.
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
        _watcher?.Stop();
        _explorerBridgeCts?.Cancel();
        _explorerBridgeCts?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _appIcon?.Dispose(); // NotifyIcon.Dispose does not free the icon handed to it
        Shutdown();
    }
}
