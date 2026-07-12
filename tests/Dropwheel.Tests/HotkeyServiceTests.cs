using System.Windows.Input;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies hotkey string parsing: valid combinations are recognized, while garbage, an
/// empty string and "modifiers only" are rejected, so a typo in settings doesn't silently disable
/// the hotkey.</summary>
public sealed class HotkeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("ctrl+alt+space")]
    [InlineData("Ctrl+Shift+F1")]
    [InlineData("Win+D")]
    [InlineData("Alt+A")]
    public void Valid_combinations_are_accepted(string hotkey)
    {
        Assert.True(HotkeyService.TryParse(hotkey, out uint mods, out uint vk));
        Assert.NotEqual(0u, mods);
        Assert.NotEqual(0u, vk);
    }

    // Russian JCUKEN layout: the user presses a physical key and gets a Cyrillic letter that looks
    // like a Latin one. It must be recognized as the same key. The InlineData below is Cyrillic on
    // purpose — that is the test input.
    [Theory]
    [InlineData("Ctrl+Alt+С", "Ctrl+Alt+C")]   // Cyrillic key at the C position -> Latin C
    [InlineData("Ctrl+Alt+В", "Ctrl+Alt+D")]   // key at the D position -> D
    [InlineData("Win+Ф", "Win+A")]              // key at the A position -> A
    public void Cyrillic_letters_map_to_the_same_physical_key(string cyrillic, string latin)
    {
        Assert.True(HotkeyService.TryParse(cyrillic, out uint m1, out uint v1));
        Assert.True(HotkeyService.TryParse(latin, out uint m2, out uint v2));
        Assert.Equal(m2, m1);
        Assert.Equal(v2, v1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]          // modifiers only, no key
    [InlineData("Ctrl+Foo")]          // nonexistent key
    [InlineData("Ctrl+A+B")]          // two keys
    [InlineData("+++")]
    public void Invalid_combinations_are_rejected(string hotkey)
    {
        Assert.False(HotkeyService.TryParse(hotkey, out _, out _));
        Assert.False(HotkeyService.IsValid(hotkey));
    }

    [Theory]
    [InlineData("control + windows + enter", "Ctrl+Win+Return")]
    [InlineData("Win+PgUp", "Win+PageUp")]
    [InlineData("ctrl+alt+space", "Ctrl+Alt+Space")]
    public void Manual_input_is_normalized_for_settings(string input, string expected)
    {
        Assert.True(HotkeyService.TryNormalize(input, out var normalized));

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Space")]
    public void Settings_normalization_requires_a_modifier(string input)
    {
        Assert.True(HotkeyService.TryParse(input, out _, out _));

        Assert.False(HotkeyService.TryNormalize(input, out _));
    }

    [Theory]
    [InlineData(Key.Space, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+Space")]
    [InlineData(Key.D, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+D")]
    [InlineData(Key.F12, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+F12")]
    public void Captured_key_strokes_are_formatted_for_settings(
        Key key,
        ModifierKeys modifiers,
        string expected)
    {
        Assert.True(HotkeyService.TryFormatCapturedHotkey(key, modifiers, out var hotkey));

        Assert.Equal(expected, hotkey);
    }

    [Theory]
    [InlineData(Key.LeftCtrl, ModifierKeys.Control)]
    [InlineData(Key.A, ModifierKeys.None)]
    public void Captured_key_strokes_need_a_modifier_and_non_modifier_key(
        Key key,
        ModifierKeys modifiers)
    {
        Assert.False(HotkeyService.TryFormatCapturedHotkey(key, modifiers, out _));
    }

    [Fact]
    public void Equivalent_hotkeys_compare_by_registered_combination()
    {
        Assert.True(HotkeyService.IsSameCombination("Control+Alt+Space", "ctrl+alt+space"));
    }
}
