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

    private const int WM_HOTKEY = 0x0312;
    /// <summary>Default hotkey id (the primary "open at cursor" shortcut). A second shortcut passes its
    /// own distinct id so both can be registered on the same window at once.</summary>
    public const int DefaultId = 0x0D27;
    private const uint ModAlt = 0x1;
    private const uint ModCtrl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private readonly HwndSource _src;
    private readonly Action _callback;
    private readonly int _id;

    public HotkeyService(Window owner, string hotkey, Action callback, int id = DefaultId)
    {
        _callback = callback;
        _id = id;
        _src = (HwndSource)PresentationSource.FromVisual(owner)!;
        // Validate and register BEFORE attaching the hook. If either step threw after AddHook, the
        // half-built instance would be discarded without Dispose, leaking the Hook delegate on the
        // window's HwndSource for the whole process — and since the hook fires for the id, every later
        // successful registration would then fire each leaked hook too.
        if (!TryParse(hotkey, out uint mods, out uint vk))
            throw new FormatException($"Hotkey '{hotkey}' is not a valid combination");
        if (!RegisterHotKey(_src.Handle, _id, mods, vk))
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
                case "alt": mods |= ModAlt; break;
                case "ctrl": mods |= ModCtrl; break;
                case "control": mods |= ModCtrl; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                case "windows": mods |= ModWin; break;
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

    /// <summary>Formats a captured key stroke as the settings editor stores it. User-facing hotkeys
    /// need at least one modifier so a stray letter cannot hijack ordinary typing.</summary>
    public static bool TryFormatCapturedHotkey(Key key, ModifierKeys modifiers, out string hotkey)
    {
        hotkey = "";
        if (IsModifierKey(key) || key == Key.None) return false;

        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= ModCtrl;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModWin;
        if (!HasModifier(mods)) return false;

        hotkey = Format(mods, key);
        return true;
    }

    /// <summary>Normalizes manual input to the same canonical form as captured input. This keeps the
    /// saved config stable while still accepting aliases such as Control or Windows.</summary>
    public static bool TryNormalize(string s, out string hotkey)
    {
        hotkey = "";
        if (!TryParse(s, out uint mods, out uint vk) || !HasModifier(mods)) return false;

        var key = KeyInterop.KeyFromVirtualKey(checked((int)vk));
        if (key == Key.None) return false;

        hotkey = Format(mods, key);
        return true;
    }

    /// <summary>Compares two hotkey strings by the virtual key they register, not by spelling.</summary>
    public static bool IsSameCombination(string left, string right) =>
        TryParse(left, out uint leftMods, out uint leftKey)
        && TryParse(right, out uint rightMods, out uint rightKey)
        && leftMods == rightMods
        && leftKey == rightKey;

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
        part = NormalizeAlias(part);
        if (part.Length != 1) return part;
        return CyrillicToLatin.TryGetValue(char.ToLowerInvariant(part[0]), out var lat)
            ? lat.ToString() : part;
    }

    private static string NormalizeAlias(string part) => part.ToLowerInvariant() switch
    {
        "esc" => "Escape",
        "enter" => "Return",
        "del" => "Delete",
        "ins" => "Insert",
        "pgup" => "Prior",
        "pageup" => "Prior",
        "pgdn" => "Next",
        "pagedown" => "Next",
        _ => part,
    };

    private static bool HasModifier(uint mods) => (mods & (ModAlt | ModCtrl | ModShift | ModWin)) != 0;

    private static string Format(uint mods, Key key)
    {
        var parts = new List<string>(5);
        if ((mods & ModCtrl) != 0) parts.Add("Ctrl");
        if ((mods & ModAlt) != 0) parts.Add("Alt");
        if ((mods & ModShift) != 0) parts.Add("Shift");
        if ((mods & ModWin) != 0) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) =>
        new KeyConverter().ConvertToInvariantString(key) ?? key.ToString();

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin;

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
        if (msg == WM_HOTKEY && checked((int)w) == _id) { _callback(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_src.Handle, _id);
        _src.RemoveHook(Hook);
    }
}
