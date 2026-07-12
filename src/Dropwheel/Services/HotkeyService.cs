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
        // Validate and register BEFORE attaching the hook. If either step threw after AddHook, the
        // half-built instance would be discarded without Dispose, leaking the Hook delegate on the
        // window's HwndSource for the whole process — and since Id is shared, every later successful
        // registration would then fire each leaked hook too.
        if (!TryParse(hotkey, out uint mods, out uint vk))
            throw new FormatException($"Hotkey '{hotkey}' is not a valid combination");
        if (!RegisterHotKey(_src.Handle, Id, mods, vk))
            throw new InvalidOperationException($"Hotkey '{hotkey}' is already taken");
        _src.AddHook(Hook);
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
                case "alt": mods |= 0x1; break;
                case "ctrl": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win": mods |= 0x8; break;
                default:
                    if (key != null) return false; // more than one non-modifier key
                    try
                    {
                        if (new KeyConverter().ConvertFromInvariantString(NormalizeKeyName(part)) is Key k) key = k;
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

    private const int TrialId = 0x0D28; // distinct from the live Id so a trial never clashes with it

    /// <summary>Whether the combination is free to register right now: a trial registration under a
    /// throwaway id, released immediately. Returns false for bad syntax or a combo another app already
    /// holds. NOTE: the app's own active hotkey reads as taken (we hold it), so callers must treat
    /// "same as the current hotkey" as available before calling this.</summary>
    public static bool IsAvailable(string s)
    {
        if (!TryParse(s, out uint mods, out uint vk)) return false;
        if (!RegisterHotKey(IntPtr.Zero, TrialId, mods, vk)) return false;
        UnregisterHotKey(IntPtr.Zero, TrialId);
        return true;
    }

    /// <summary>Russian (JCUKEN) layout: a single Cyrillic letter maps to the Latin key at the same
    /// physical position. Without this a user on a Russian layout types a letter that looks right in
    /// the hotkey field, but KeyConverter can't parse it and the combo is wrongly treated as broken.
    /// The virtual key is registered by position anyway, so the shortcut fires under any active
    /// layout. The map keys below are Cyrillic on purpose — that is the layout data.</summary>
    private static string NormalizeKeyName(string part)
    {
        if (part.Length != 1) return part;
        return CyrillicToLatin.TryGetValue(char.ToLowerInvariant(part[0]), out var lat)
            ? lat.ToString() : part;
    }

    private static readonly Dictionary<char, char> CyrillicToLatin = new()
    {
        ['й'] = 'q',
        ['ц'] = 'w',
        ['у'] = 'e',
        ['к'] = 'r',
        ['е'] = 't',
        ['н'] = 'y',
        ['г'] = 'u',
        ['ш'] = 'i',
        ['щ'] = 'o',
        ['з'] = 'p',
        ['ф'] = 'a',
        ['ы'] = 's',
        ['в'] = 'd',
        ['а'] = 'f',
        ['п'] = 'g',
        ['р'] = 'h',
        ['о'] = 'j',
        ['л'] = 'k',
        ['д'] = 'l',
        ['я'] = 'z',
        ['ч'] = 'x',
        ['с'] = 'c',
        ['м'] = 'v',
        ['и'] = 'b',
        ['т'] = 'n',
        ['ь'] = 'm',
    };

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
