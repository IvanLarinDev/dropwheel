using System.Windows.Input;
using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class OrbGestureTests
{
    [Fact]
    public void No_modifier_means_no_drag_action()
    {
        Assert.Equal(OrbDragKind.None, OrbGesture.Classify(ModifierKeys.None));
    }

    [Fact]
    public void Alt_alone_moves_the_orb()
    {
        Assert.Equal(OrbDragKind.Move, OrbGesture.Classify(ModifierKeys.Alt));
    }

    [Fact]
    public void Alt_and_shift_capture_the_object_under_the_cursor()
    {
        Assert.Equal(OrbDragKind.Capture, OrbGesture.Classify(ModifierKeys.Alt | ModifierKeys.Shift));
    }

    [Fact]
    public void Shift_without_alt_is_not_an_orb_gesture()
    {
        Assert.Equal(OrbDragKind.None, OrbGesture.Classify(ModifierKeys.Shift));
    }

    [Fact]
    public void Ctrl_riding_along_does_not_change_capture()
    {
        Assert.Equal(OrbDragKind.Capture,
            OrbGesture.Classify(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control));
    }
}
