using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Проверяет разбор строки горячей клавиши: валидные комбинации распознаются,
/// а мусор, пустая строка и «только модификаторы» отвергаются, чтобы опечатка в настройках
/// не отключала хоткей молча.</summary>
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

    // Русская раскладка ЙЦУКЕН: пользователь жмёт физическую клавишу и получает кириллическую
    // букву, визуально совпадающую с латинской. Она должна распознаваться как та же клавиша.
    [Theory]
    [InlineData("Ctrl+Alt+С", "Ctrl+Alt+C")]   // С кириллическая → латинская C
    [InlineData("Ctrl+Alt+В", "Ctrl+Alt+D")]   // В → D (по позиции на клавиатуре)
    [InlineData("Win+Ф", "Win+A")]              // Ф → A
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
    [InlineData("Ctrl+Alt")]          // только модификаторы, без клавиши
    [InlineData("Ctrl+Foo")]          // несуществующая клавиша
    [InlineData("Ctrl+A+B")]          // две клавиши
    [InlineData("+++")]
    public void Invalid_combinations_are_rejected(string hotkey)
    {
        Assert.False(HotkeyService.TryParse(hotkey, out _, out _));
        Assert.False(HotkeyService.IsValid(hotkey));
    }
}
