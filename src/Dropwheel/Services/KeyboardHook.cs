using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dropwheel.Services;

/// <summary>Low-level keyboard hook used only to observe bare digits while the orb has armed group
/// navigation. All other input is passed through untouched.</summary>
public sealed class KeyboardHook : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr hMod, uint tid);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly Func<char, bool> _onDigit;
    private readonly HashSet<uint> _capturedKeys = [];
    private HookProc? _proc;
    private IntPtr _hook;

    public KeyboardHook(Func<char, bool> onDigit) => _onDigit = onDigit;

    public bool Start()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = Callback;
        _hook = SetWindowsHookExW(WhKeyboardLl, _proc, GetModuleHandleW(null), 0);
        if (_hook != IntPtr.Zero) return true;

        ErrorLog.Write("Failed to install the group shortcut keyboard hook",
            new Win32Exception(Marshal.GetLastWin32Error()));
        _proc = null;
        return false;
    }

    internal static bool TryGetDigit(uint virtualKey, out char digit)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            digit = (char)('0' + virtualKey - 0x30);
            return true;
        }
        if (virtualKey is >= 0x60 and <= 0x69)
        {
            digit = (char)('0' + virtualKey - 0x60);
            return true;
        }
        digit = default;
        return false;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (TryGetDigit(data.VkCode, out var digit))
            {
                if (message is WmKeyDown or WmSysKeyDown)
                {
                    if (_capturedKeys.Contains(data.VkCode)) return new IntPtr(1);
                    try
                    {
                        if (_onDigit(digit))
                        {
                            _capturedKeys.Add(data.VkCode);
                            return new IntPtr(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Write("Failed to process a group shortcut digit", ex);
                    }
                }
                else if ((message is WmKeyUp or WmSysKeyUp) && _capturedKeys.Remove(data.VkCode))
                {
                    return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _capturedKeys.Clear();
        _proc = null;
    }
}
