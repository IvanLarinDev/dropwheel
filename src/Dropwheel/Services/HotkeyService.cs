using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Dropwheel.Services;

/// <summary>Глобальный хоткей через RegisterHotKey. Строка вида "Ctrl+Alt+Space".</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312, Id = 0x0D27;
    private readonly HwndSource _src;
    private readonly Action _callback;

    public HotkeyService(Window owner, string hotkey, Action callback)
    {
        _callback = callback;
        _src = (HwndSource)PresentationSource.FromVisual(owner)!;
        _src.AddHook(Hook);
        var (mods, vk) = Parse(hotkey);
        if (!RegisterHotKey(_src.Handle, Id, mods, vk))
            throw new InvalidOperationException($"Hotkey '{hotkey}' is already taken");
    }

    private static (uint Mods, uint Vk) Parse(string s)
    {
        uint mods = 0;
        var key = Key.Space;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            switch (part.ToLowerInvariant())
            {
                case "alt":   mods |= 0x1; break;
                case "ctrl":  mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win":   mods |= 0x8; break;
                default:
                    key = (Key)(new KeyConverter().ConvertFromInvariantString(part)
                        ?? throw new FormatException(part));
                    break;
            }
        return (mods, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)w == Id) { _callback(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_src.Handle, Id);
        _src.RemoveHook(Hook);
    }
}
