using System.Windows;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow : Window
{
    private const double HalfSize = 230; // 460x460 window, orb centered

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _closeTimer;
    private readonly DispatcherTimer _toastTimer;
    private bool _open;

    public OverlayWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(TargetStore.Config.HoverDelayMs) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); OpenCloud(); };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); CloseCloud(); };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

        Orb.Opacity = TargetStore.Config.OrbOpacity;
        Orb.MouseEnter += (_, _) => { if (!_open) _hoverTimer.Start(); };
        Orb.MouseLeave += (_, _) => _hoverTimer.Stop();
        Orb.MouseLeftButtonDown += OnOrbMouseDown;
        Orb.DragEnter += (_, _) => { _closeTimer.Stop(); OpenCloud(); };
        Orb.Drop += OnOrbDrop; // dropping on the orb adds a target

        var orbMenu = new System.Windows.Controls.ContextMenu();
        var newGroup = new System.Windows.Controls.MenuItem { Header = "New group…" };
        newGroup.Click += (_, _) => CreateGroup();
        var settings = new System.Windows.Controls.MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => OpenSettings();
        orbMenu.Items.Add(newGroup);
        orbMenu.Items.Add(settings);
        Orb.ContextMenu = orbMenu;

        Root.MouseEnter += (_, _) => _closeTimer.Stop();
        Root.MouseLeave += (_, _) => { if (_open) _closeTimer.Start(); };
        DragEnter += (_, _) => _closeTimer.Stop();
        DragLeave += (_, _) => { if (_open) _closeTimer.Start(); };
        Deactivated += (_, _) => CloseCloud();

        Loaded += (_, _) =>
        { PlaceWindow(); PaintHub(); InitProximity(); InitHotkeyAndFullscreen(); InitIdleFade(); };
        LocationChanged += (_, _) => UpdateOrbScreenPos();
    }
}
