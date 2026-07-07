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
}
