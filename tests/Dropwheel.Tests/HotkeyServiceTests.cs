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
