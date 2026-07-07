using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Dropwheel.Services;

/// <summary>Global hotkey via RegisterHotKey. String form: "Ctrl+Alt+Space".</summary>
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
        if (!TryParse(hotkey, out uint mods, out uint vk))
            throw new FormatException($"Hotkey '{hotkey}' is not a valid combination");
        if (!RegisterHotKey(_src.Handle, Id, mods, vk))
            throw new InvalidOperationException($"Hotkey '{hotkey}' is already taken");
    }

    /// <summary>Parses a combination like "Ctrl+Alt+Space" into RegisterHotKey modifier flags and a
    /// virtual-key code. Returns false for an empty string, an unknown key name, or a combination
    /// with only modifiers and no actual key — so a typo cannot silently disable the hotkey.</summary>
    public static bool TryParse(string s, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        Key? key = null;
        foreach (var part in s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            switch (part.ToLowerInvariant())
            {
                case "alt":   mods |= 0x1; break;
                case "ctrl":  mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win":   mods |= 0x8; break;
                default:
                    if (key != null) return false; // more than one non-modifier key
                    try
                    {
                        if (new KeyConverter().ConvertFromInvariantString(part) is Key k) key = k;
                        else return false;
                    }
                    catch (Exception) { return false; } // KeyConverter rejects unknown names
                    break;
            }
        if (key == null) return false; // modifiers only, no key to press
        vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return vk != 0;
    }

    /// <summary>Whether the string is a hotkey the app can actually register.</summary>
    public static bool IsValid(string s) => TryParse(s, out _, out _);

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
