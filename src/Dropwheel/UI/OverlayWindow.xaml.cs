using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dropwheel.Services;

namespace Dropwheel.UI;

public partial class OverlayWindow : Window
{
    /// <summary>Wheel center in window coordinates. The window is square and the orb sits at its
    /// center, so this is always half the current width. The window is 460 while the wheel is
    /// closed and grows only while an overflow level is open (see ApplyWheelWindow).</summary>
    private double HalfSize => Width / 2;

    /// <summary>Hover timer interval, guarded against zero: a zero interval makes DispatcherTimer
    /// tick on every dispatcher pass, so we keep a sensible minimum.</summary>
    private static TimeSpan HoverInterval() =>
        TimeSpan.FromMilliseconds(Math.Max(50, TargetStore.Config.HoverDelayMs));

    /// <summary>Whether the physical mouse cursor is inside the window's current screen bounds. Used to
    /// tell a real mouse-leave from a spurious one that WPF raises when we move/resize the window under
    /// a stationary cursor.</summary>
    private bool CursorInsideWindow()
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } ct) return false;
        var c = System.Windows.Forms.Cursor.Position; // device px
        var p = ct.TransformFromDevice.Transform(new Point(c.X, c.Y)); // → DIP, window/screen space
        return p.X >= Left && p.X < Left + Width && p.Y >= Top && p.Y < Top + Height;
    }

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _closeTimer;
    private readonly DispatcherTimer _toastTimer;
    private bool _open;

    public OverlayWindow()
    {
        InitializeComponent();

        _hoverTimer = new DispatcherTimer { Interval = HoverInterval() };
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            ErrorLog.Trace($"hover-timer-fire alt={Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)} open={_open}");
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) OpenCloud("hover");
        };

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            // A programmatic resize/move of the window slides it out from under a still cursor, and WPF
            // fires a spurious MouseLeave. Only close if the pointer is physically outside the window,
            // otherwise the wheel would flicker open/closed while the cursor sits on the orb.
            if (CursorInsideWindow()) { ErrorLog.Trace("close-suppressed (cursor still inside window)"); return; }
            CloseCloud("leave-timer");
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

        Orb.Opacity = TargetStore.Config.OrbOpacity;
        Orb.MouseEnter += (_, _) =>
        {
            bool arm = !_open && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            ErrorLog.Trace($"orb-enter open={_open} hover-arm={arm}");
            ArmGroupShortcuts();
            if (arm) _hoverTimer.Start();
        };
        Orb.MouseLeave += (_, _) =>
        {
            ErrorLog.Trace($"orb-leave open={_open} hover-was-armed={_hoverTimer.IsEnabled}");
            _hoverTimer.Stop();
            OnOrbGroupShortcutLeave();
        };
        Orb.MouseLeftButtonDown += OnOrbMouseDown;
        Orb.DragEnter += (_, _) => { _closeTimer.Stop(); OpenCloud("orb-dragenter"); };
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

        Root.MouseEnter += (_, _) => { if (_open) ErrorLog.Trace("root-enter close-stop"); _closeTimer.Stop(); };
        Root.MouseLeave += (_, _) =>
        {
            ResetGroupShortcutInput(preserveActivation: false);
            if (_open) { ErrorLog.Trace("root-leave close-start"); _closeTimer.Start(); }
        };
        AllowDrop = true;
        PreviewDragOver += OnTileReorderPreviewDragOver;
        PreviewDrop += OnTileReorderPreviewDrop;
        DragEnter += (_, _) => _closeTimer.Stop();
        DragLeave += (_, _) => { if (_open) _closeTimer.Start(); };
        Deactivated += (_, _) => { ErrorLog.Trace("deactivated (focus lost)"); CloseCloud("deactivated"); };
        Activated += (_, _) => ErrorLog.Trace("activated (focus gained)");
        SizeChanged += (_, e) => ErrorLog.Trace($"window-size {e.NewSize.Width:0}x{e.NewSize.Height:0}");

        Loaded += (_, _) =>
        {
            ErrorLog.Trace("=== session start ===");
            PlaceWindow(); PaintHub(); InitProximity(); InitHotkeyAndFullscreen(); InitGroupShortcuts(); InitIdleFade();
        };
        LocationChanged += (_, _) =>
        {
            if (!_movingOrb) ErrorLog.Trace($"window-move L={Left:0} T={Top:0} open={_open}");
            UpdateOrbScreenPos();
        };
    }
}
