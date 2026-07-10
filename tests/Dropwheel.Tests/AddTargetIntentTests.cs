using System.Windows;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class AddTargetIntentTests
{
    [Fact]
    public void Shift_pins_the_new_target()
    {
        Assert.True(AddTargetIntent.ShouldPin(DragDropKeyStates.ShiftKey));
    }

    [Fact]
    public void Shift_still_pins_when_combined_with_other_keys()
    {
        var keys = DragDropKeyStates.ShiftKey | DragDropKeyStates.ControlKey | DragDropKeyStates.LeftMouseButton;

        Assert.True(AddTargetIntent.ShouldPin(keys));
    }

    [Theory]
    [InlineData(DragDropKeyStates.None)]
    [InlineData(DragDropKeyStates.ControlKey)]
    [InlineData(DragDropKeyStates.AltKey)]
    [InlineData(DragDropKeyStates.LeftMouseButton)]
    public void Without_shift_the_target_is_added_normally(DragDropKeyStates keys)
    {
        Assert.False(AddTargetIntent.ShouldPin(keys));
    }
}
