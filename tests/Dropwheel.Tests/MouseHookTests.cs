using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class MouseHookTests
{
    [Theory]
    [InlineData(MouseHook.WM_LBUTTONDOWN, false, true)]
    [InlineData(MouseHook.WM_LBUTTONDOWN, true, true)]
    [InlineData(MouseHook.WM_LBUTTONUP, true, false)]
    [InlineData(MouseHook.WM_LBUTTONUP, false, false)]
    public void Button_messages_set_left_state(int message, bool current, bool expected)
    {
        Assert.Equal(expected, MouseHook.NextLeftDown(message, current));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Move_messages_keep_left_state(bool current)
    {
        Assert.Equal(current, MouseHook.NextLeftDown(MouseHook.WM_MOUSEMOVE, current));
    }

    [Fact]
    public void Dispose_without_start_is_safe_and_idempotent()
    {
        var hook = new MouseHook();
        hook.Dispose();
        hook.Dispose();
    }
}
