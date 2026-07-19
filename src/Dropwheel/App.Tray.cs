using System.Diagnostics;
using Dropwheel.Services;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace Dropwheel;

public partial class App
{
    // Runtime-only pauses from the tray, both reset on restart. "Auto-sort" stops only the background
    // watcher; "Sorting" also makes a manual drop on a sorter skip the rules. _watcherRunning tracks the
    // watcher so Start/Stop are idempotent — Start twice would double-subscribe its config-save handler.
    private bool _autoSortPaused;
    private bool _sortingPaused;
    private bool _watcherRunning = true;

    /// <summary>Applies both pauses: the watcher runs only when neither pause is on (idempotent), and a
    /// manual sorter drop skips the rules only under the full "Pause sorting".</summary>
    private void ApplyPauseState()
    {
        bool shouldRun = !_autoSortPaused && !_sortingPaused;
        if (shouldRun && !_watcherRunning) { _watcher?.Start(); _watcherRunning = true; }
        else if (!shouldRun && _watcherRunning) { _watcher?.Stop(); _watcherRunning = false; }
        DropDispatch.SortingPaused = _sortingPaused;
    }

    // Segoe MDL2 Assets glyphs for the menu icons. A checked toggle shows the accent check instead
    // of its own glyph, so the on/off state reads at a glance without the stock check boxes.
    private const string GlyphCheck = "\uE73E"; // CheckMark
    private const string GlyphPower = "\uE7E8"; // PowerButton
    private const string GlyphSend = "\uE724"; // Send
    private const string GlyphPause = "\uE769"; // Pause
    private const string GlyphSettings = "\uE713"; // Setting
    private const string GlyphFolder = "\uE8B7"; // Folder
    private const string GlyphExport = "\uE898"; // Upload
    private const string GlyphReload = "\uE72C"; // Refresh
    private const string GlyphHistory = "\uE81C"; // History
    private const string GlyphHelp = "\uE897"; // Help
    private const string GlyphExit = "\uE8BB"; // ChromeClose

    private static readonly Dictionary<(string Glyph, SD.Color Color), SD.Bitmap> GlyphCache = new();

    /// <summary>Small menu icon rendered from a Segoe MDL2 Assets glyph in the given color. Cached
    /// forever — the set of glyph+color pairs is tiny and the bitmaps are reused on every menu open.</summary>
    private static SD.Bitmap Glyph(string glyph, SD.Color color)
    {
        if (GlyphCache.TryGetValue((glyph, color), out var cached)) return cached;
        var size = WF.SystemInformation.SmallIconSize;
        var bmp = new SD.Bitmap(size.Width, size.Height);
        using (var g = SD.Graphics.FromImage(bmp))
        using (var font = new SD.Font("Segoe MDL2 Assets", size.Height * 0.62f, SD.GraphicsUnit.Pixel))
        using (var brush = new SD.SolidBrush(color))
        using (var format = new SD.StringFormat
        { Alignment = SD.StringAlignment.Center, LineAlignment = SD.StringAlignment.Center })
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.DrawString(glyph, font, brush, new SD.RectangleF(0, 0, size.Width, size.Height), format);
        }
        GlyphCache[(glyph, color)] = bmp;
        return bmp;
    }

    /// <summary>A toggle shows the accent check when on and its own muted glyph when off — the state
    /// lives in the icon slot, replacing the stock check boxes that clash with the dark theme.</summary>
    private static void SetToggleIcon(WF.ToolStripMenuItem item, string glyph)
    {
        var p = Dropwheel.UI.Palettes.Current;
        item.Image = item.Checked ? Glyph(GlyphCheck, ToSd(p.Accent)) : Glyph(glyph, ToSd(p.TextMuted));
    }

    private void InitTray(bool maintainSystemIntegrations)
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
        {
            Checked = maintainSystemIntegrations && StartupService.IsEnabled,
            CheckOnClick = true,
            Enabled = maintainSystemIntegrations,
        };
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
        if (maintainSystemIntegrations)
        {
            try
            {
                if (ExplorerBridgeService.NeedsSendToUpgrade())
                    ExplorerBridgeService.InstallSendTo(CurrentAppPath());
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not upgrade the Explorer SendTo shortcut", ex);
            }
        }
        var sendTo = new WF.ToolStripMenuItem("Explorer SendTo shortcut")
        {
            Checked = maintainSystemIntegrations && ExplorerBridgeService.IsSendToInstalled(),
            CheckOnClick = true,
            Enabled = maintainSystemIntegrations,
        };
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
        menu.Items.Add(new WF.ToolStripSeparator());
        var pauseAuto = new WF.ToolStripMenuItem("Pause auto-sort (watched folders)")
        {
            Checked = _autoSortPaused,
            CheckOnClick = true,
            ToolTipText = "Temporarily stop watched folders from auto-sorting in the background. A manual "
                        + "drop on a sorter still sorts. Files pile up in the folder and are swept when "
                        + "resumed. Resets on restart; the Watch setting is untouched.",
        };
        pauseAuto.Click += (_, _) =>
        {
            _autoSortPaused = pauseAuto.Checked;
            ApplyPauseState();
            _tray?.ShowBalloonTip(2500, "Dropwheel",
                _autoSortPaused ? "Auto-sort paused — watched folders won't sort until you resume."
                                : "Auto-sort resumed.",
                WF.ToolTipIcon.Info);
        };
        menu.Items.Add(pauseAuto);
        var pauseSort = new WF.ToolStripMenuItem("Pause sorting (drops too)")
        {
            Checked = _sortingPaused,
            CheckOnClick = true,
            ToolTipText = "Temporarily stop applying sorter rules everywhere — watched folders and a manual "
                        + "drop on a sorter. Files just land in the folder undistributed. Resets on restart; "
                        + "the Watch and rule settings are untouched.",
        };
        pauseSort.Click += (_, _) =>
        {
            _sortingPaused = pauseSort.Checked;
            ApplyPauseState();
            _tray?.ShowBalloonTip(2500, "Dropwheel",
                _sortingPaused ? "Sorting paused — files land in the folder without being distributed."
                               : "Sorting resumed.",
                WF.ToolTipIcon.Info);
        };
        menu.Items.Add(pauseSort);
        menu.Items.Add(new WF.ToolStripSeparator());
        var settings = (WF.ToolStripMenuItem)menu.Items.Add("Settings…", null, (_, _) => _overlay?.OpenSettings());
        var configFolder = (WF.ToolStripMenuItem)menu.Items.Add("Open config folder", null,
            (_, _) => LaunchService.OpenConfigFolder());
        var export = (WF.ToolStripMenuItem)menu.Items.Add("Export settings…", null, (_, _) => ExportConfig());
        var reload = new WF.ToolStripMenuItem("Reload settings")
        {
            ToolTipText = "Re-read config.json from disk after a manual edit and apply it without "
                        + "restarting. A broken file is reported and the current settings stay untouched.",
        };
        reload.Click += (_, _) => ReloadConfig();
        menu.Items.Add(reload);
        menu.Items.Add(new WF.ToolStripSeparator());
        var recentDrops = new WF.ToolStripMenuItem("Recent drops");
        menu.Items.Add(recentDrops);
        menu.Items.Add(new WF.ToolStripSeparator());
        var help = new WF.ToolStripMenuItem("Help");
        var quickStart = new WF.ToolStripMenuItem("Quick start…");
        quickStart.Click += (_, _) => ShowOnboarding();
        help.DropDownItems.Add(quickStart);
        menu.Items.Add(help);
        var exit = (WF.ToolStripMenuItem)menu.Items.Add("Exit", null, (_, _) => ExitApp());

        // Re-tinted on every open so a theme change recolors them live; toggles re-read their state.
        void RefreshTrayIcons()
        {
            var muted = ToSd(Dropwheel.UI.Palettes.Current.TextMuted);
            SetToggleIcon(auto, GlyphPower);
            SetToggleIcon(sendTo, GlyphSend);
            SetToggleIcon(pauseAuto, GlyphPause);
            SetToggleIcon(pauseSort, GlyphPause);
            settings.Image = Glyph(GlyphSettings, muted);
            configFolder.Image = Glyph(GlyphFolder, muted);
            export.Image = Glyph(GlyphExport, muted);
            reload.Image = Glyph(GlyphReload, muted);
            recentDrops.Image = Glyph(GlyphHistory, muted);
            help.Image = Glyph(GlyphHelp, muted);
            exit.Image = Glyph(GlyphExit, muted);
        }

        RefreshTrayIcons();
        StyleTrayMenu(menu);
        menu.Opening += (_, _) =>
        {
            fsStatus.Visible = Dropwheel.Services.FullscreenDetector.IsFullscreenActive();
            if (maintainSystemIntegrations)
            {
                auto.Checked = StartupService.IsEnabled;
                sendTo.Checked = ExplorerBridgeService.IsSendToInstalled();
            }
            PopulateRecentDrops(recentDrops);
            StyleTrayMenu(menu);
            RefreshTrayIcons();
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

    /// <summary>Copies config.json to a user-chosen file as a backup or for moving to another machine.
    /// The WinForms dialog is used on purpose — this runs from a WinForms tray-menu click.</summary>
    private void ExportConfig()
    {
        using var dialog = new WF.SaveFileDialog
        {
            Title = "Export Dropwheel settings",
            FileName = $"dropwheel-config_{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "JSON settings (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
        };
        if (dialog.ShowDialog() != WF.DialogResult.OK) return;
        try
        {
            // The dialog has already asked about overwriting, so the copy may replace the file.
            System.IO.File.Copy(TargetStore.FilePath, dialog.FileName, overwrite: true);
            _tray?.ShowBalloonTip(2500, "Dropwheel",
                $"Settings exported to {dialog.FileName}.", WF.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Could not export settings to '{dialog.FileName}'", ex);
            _tray?.ShowBalloonTip(4000, "Dropwheel",
                "Couldn't export settings. See error.log for details.", WF.ToolTipIcon.Warning);
        }
    }

    /// <summary>Re-reads config.json from disk and applies it live. A parse error keeps the current
    /// settings and shows what went wrong, so a typo in a hand-edited file can't wipe anything.</summary>
    private void ReloadConfig()
    {
        if (TargetStore.TryReload(out var error))
        {
            _overlay?.ApplySettings();
            _tray?.ShowBalloonTip(2500, "Dropwheel", "Settings reloaded from disk.", WF.ToolTipIcon.Info);
            return;
        }
        ErrorLog.Write($"Reload settings failed: {error}");
        _tray?.ShowBalloonTip(6000, "Dropwheel",
            $"Settings NOT reloaded — the file has a problem, current settings are kept.\n{error}",
            WF.ToolTipIcon.Warning);
    }

    /// <summary>Puts the recent-drops list on the clipboard as plain text. Uses the WinForms clipboard —
    /// this runs from a WinForms tray-menu click, where the WPF clipboard can misbehave. The clipboard
    /// occasionally refuses when another app holds it, so a few quick retries before a balloon.</summary>
    private void CopyDropHistoryList()
    {
        var text = DropHistoryService.ClipboardText(DropHistoryService.LoadForMenu());
        if (string.IsNullOrEmpty(text)) return;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                WF.Clipboard.SetText(text);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= 2)
                {
                    ErrorLog.Write("Could not copy the drop history to the clipboard", ex);
                    _tray?.ShowBalloonTip(4000, "Dropwheel",
                        "Couldn't copy the list — the clipboard is busy.", WF.ToolTipIcon.Warning);
                    return;
                }
                System.Threading.Thread.Sleep(60);
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
    /// Re-applied on each open so a theme change takes effect live. Light themes get the same custom
    /// renderer as dark ones — the stock one would draw its own check boxes over the glyph icons.</summary>
    private static void StyleTrayMenu(WF.ContextMenuStrip menu)
    {
        var p = Dropwheel.UI.Palettes.Current;
        StyleStrip(menu, new TrayMenuRenderer(new PaletteMenuColors(p)), ToSd(p.Surface), ToSd(p.Text));
    }

    /// <summary>Applies the renderer and colors to the strip and every nested drop-down. A submenu is
    /// its own ToolStrip that does not inherit the parent's colors — without this it keeps the stock
    /// light look, unreadable next to a dark main menu.</summary>
    private static void StyleStrip(WF.ToolStrip strip, WF.ToolStripRenderer renderer, SD.Color back, SD.Color fore)
    {
        strip.Renderer = renderer;
        strip.BackColor = back;
        strip.ForeColor = fore;
        foreach (WF.ToolStripItem item in strip.Items)
            if (item is WF.ToolStripDropDownItem dd)
                StyleStrip(dd.DropDown, renderer, back, fore);
    }

    /// <summary>The checked state is already shown by the item's accent check glyph (SetToggleIcon),
    /// so the stock check box that would be drawn behind it is suppressed entirely.</summary>
    private sealed class TrayMenuRenderer : WF.ToolStripProfessionalRenderer
    {
        public TrayMenuRenderer(WF.ProfessionalColorTable table) : base(table) => RoundedEdges = false;

        protected override void OnRenderItemCheck(WF.ToolStripItemImageRenderEventArgs e) { }
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

    private sealed class PaletteMenuColors : WF.ProfessionalColorTable
    {
        private readonly SD.Color _bg, _hover, _pressed, _line;

        public PaletteMenuColors(Dropwheel.UI.Palette p)
        {
            _bg = ToSd(p.Surface);
            _hover = Blend(p.Surface, p.Accent, 0.30);
            // An item whose submenu is open is drawn "pressed" — without these overrides the stock
            // near-white gradient kicks in and light text on it becomes unreadable.
            _pressed = Blend(p.Surface, p.Accent, 0.18);
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
        public override SD.Color MenuItemPressedGradientBegin => _pressed;
        public override SD.Color MenuItemPressedGradientMiddle => _pressed;
        public override SD.Color MenuItemPressedGradientEnd => _pressed;
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
        _ = RequestShutdownAsync();
    }
}
