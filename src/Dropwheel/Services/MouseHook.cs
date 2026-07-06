using System.Runtime.InteropServices;

namespace Dropwheel.Services;

/// <summary>Low-level mouse hook (WH_MOUSE_LL): lets the wheel open when a drag
/// approaches the orb, before the cursor enters our window.</summary>
public static class MouseHook
{
    public delegate void MouseMoveHandler(int x, int y, bool leftDown);
    public static event MouseMoveHandler? MouseMoved;

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
    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

    private static IntPtr _hook = IntPtr.Zero;
    private static HookProc? _proc; // keep a reference so the delegate is not GC'd
    private static bool _leftDown;

    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = SetWindowsHookExW(WH_MOUSE_LL, _proc, GetModuleHandleW(null), 0);
    }

    public static void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN) _leftDown = true;
            else if (msg == WM_LBUTTONUP) _leftDown = false;
            else if (msg == WM_MOUSEMOVE && MouseMoved is { } h)
            {
                var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                h(s.pt.X, s.pt.Y, _leftDown);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }
}
