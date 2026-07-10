using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class KeyboardHookTests
{
    [Theory]
    [InlineData(0x30u, '0')]
    [InlineData(0x39u, '9')]
    [InlineData(0x60u, '0')]
    [InlineData(0x69u, '9')]
    public void Top_row_and_numpad_keys_map_to_digits(uint virtualKey, char expected)
    {
        Assert.True(KeyboardHook.TryGetDigit(virtualKey, out var digit));
        Assert.Equal(expected, digit);
    }

    [Theory]
    [InlineData(0x2Fu)]
    [InlineData(0x3Au)]
    [InlineData(0x5Au)]
    [InlineData(0x6Au)]
    public void Non_digit_keys_are_ignored(uint virtualKey)
    {
        Assert.False(KeyboardHook.TryGetDigit(virtualKey, out _));
    }
}
