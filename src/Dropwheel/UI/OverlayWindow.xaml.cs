using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Window alive for the whole process: the hooks and both hotkeys are disposed in the Closed handler; a WPF Window is not idiomatically IDisposable.")]
public partial class OverlayWindow : Window
{
    /// <summary>Wheel center in window coordinates. The window is square and the orb sits at its
    /// center, so this is always half the width. The window is fixed per overflow mode for the whole
    /// session (see ApplyModeWindow) and does not resize while the wheel opens or closes.</summary>
    private double HalfSize => Width / 2;

    /// <summary>Hover timer interval, guarded against zero: a zero interval makes DispatcherTimer
    /// tick on every dispatcher pass, so we keep a sensible minimum.</summary>
    private static TimeSpan HoverInterval() =>
        TimeSpan.FromMilliseconds(Math.Max(50, TargetStore.Config.HoverDelayMs));

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _closeTimer;
    private bool _open;
    private DateTime _ignoreDeactivateUntilUtc = DateTime.MinValue;

    public OverlayWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = HoverInterval() };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) OpenCloud();
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); CloseCloud(); };

        Orb.Opacity = TargetStore.Config.OrbOpacity;
        Orb.MouseEnter += (_, _) =>
        {
            ArmGroupShortcuts();
            if (!_open && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) _hoverTimer.Start();
        };
        Orb.MouseLeave += (_, _) => { _hoverTimer.Stop(); OnOrbGroupShortcutLeave(); };
        Orb.MouseLeftButtonDown += OnOrbMouseDown;
        Orb.DragEnter += (_, _) => { _closeTimer.Stop(); OpenCloud(); };
        Orb.DragOver += OnAddTargetDragOver;
        Orb.Drop += OnOrbDrop; // dropping on the orb adds a target

        var orbMenu = new System.Windows.Controls.ContextMenu();
        var newGroup = new System.Windows.Controls.MenuItem { Header = "New group…" };
        newGroup.Click += (_, _) => CreateGroup();
        var settings = new System.Windows.Controls.MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();
        orbMenu.Items.Add(newGroup);
        orbMenu.Items.Add(settings);
        Themes.ApplyMenu(orbMenu);
        Orb.ContextMenu = orbMenu;

        Root.MouseEnter += (_, _) => _closeTimer.Stop();
        Root.MouseLeave += (_, _) =>
        {
            ResetGroupShortcutInput(preserveActivation: false);
            if (_open) _closeTimer.Start();
        };
        AllowDrop = true;
        PreviewDragOver += OnTileReorderPreviewDragOver;
        PreviewDrop += OnTileReorderPreviewDrop;
        DragEnter += (_, _) => _closeTimer.Stop();
        DragLeave += (_, _) => { if (_open) _closeTimer.Start(); };
        PreviewKeyDown += OnOverlayPreviewKeyDown;
        Deactivated += (_, _) =>
        {
            if (DateTime.UtcNow <= _ignoreDeactivateUntilUtc)
            {
                _ignoreDeactivateUntilUtc = DateTime.MinValue;
                return;
            }
            _ignoreDeactivateUntilUtc = DateTime.MinValue;
            CloseCloud();
        };

        Loaded += (_, _) =>
        { ApplyModeWindow(); PaintHub(); InitProximity(); InitHotkeyAndFullscreen(); InitGroupShortcuts(); InitIdleFade(); };
        LocationChanged += (_, _) => UpdateOrbScreenPos();
    }
}
