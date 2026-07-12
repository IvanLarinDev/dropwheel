using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dropwheel.Services;

/// <summary>Low-level mouse hook (WH_MOUSE_LL): lets the wheel open when a drag
/// approaches the orb, before the cursor enters our window.</summary>
public sealed class MouseHook : IDisposable
{
    public delegate void MouseMoveHandler(int x, int y, bool leftDown);
    public event MouseMoveHandler? MouseMoved;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr hMod, uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    private const int WH_MOUSE_LL = 14;
    internal const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

    private IntPtr _hook;
    private HookProc? _proc; // keep a reference so the delegate is not GC'd
    private bool _leftDown;

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = Callback;
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
        if (_hook != IntPtr.Zero) return true;

        ErrorLog.Write("Failed to install the proximity mouse hook",
            new Win32Exception(Marshal.GetLastWin32Error()));
        _proc = null;
        return false;
    }

    /// <summary>Tracks the left-button state across hook messages. Pure so it can be tested.</summary>
    internal static bool NextLeftDown(int message, bool current) => message switch
    {
        WM_LBUTTONDOWN => true,
        WM_LBUTTONUP => false,
        _ => current,
    };

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = checked((int)wParam);
            _leftDown = NextLeftDown(msg, _leftDown);
            if (msg == WM_MOUSEMOVE && MouseMoved is { } h)
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                // This runs inside an OS-invoked hook callback: an exception escaping here unwinds
                // through a native frame and terminates the process (the app's DispatcherUnhandledException
                // net cannot catch it), so a latent bug in a subscriber would crash on any mouse move.
                // Swallow-and-log, exactly as the sibling KeyboardHook does.
                try { h(s.pt.X, s.pt.Y, _leftDown); }
                catch (Exception ex) { ErrorLog.Write("A mouse-move subscriber threw in the low-level hook", ex); }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }
}
