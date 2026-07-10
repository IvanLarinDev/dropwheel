using Dropwheel.Services;

namespace Dropwheel.Tests;

public sealed class GroupShortcutActivationTests
{
    [Fact]
    public void Activation_survives_group_navigation_while_wheel_stays_open()
    {
        var activation = new GroupShortcutActivation();
        activation.PointerEntered();
        activation.Refresh(hasCodes: true);

        activation.ResetInput(preserveActivation: true, wheelOpen: true, hasCodes: true);
        activation.PointerLeft(wheelOpen: true, inputPending: false);

        Assert.True(activation.CanAcceptDigit(wheelOpen: true, inputPending: false));
    }

    [Fact]
    public void Activation_ends_after_pointer_and_closed_wheel_are_both_gone()
    {
        var activation = new GroupShortcutActivation();
        activation.PointerEntered();
        activation.Refresh(hasCodes: true);

        activation.PointerLeft(wheelOpen: false, inputPending: false);

        Assert.False(activation.CanAcceptDigit(wheelOpen: false, inputPending: false));
    }

    [Fact]
    public void Closing_wheel_keeps_activation_only_when_pointer_is_still_over_orb()
    {
        var activation = new GroupShortcutActivation();
        activation.PointerEntered();
        activation.Refresh(hasCodes: true);

        activation.ResetInput(preserveActivation: true, wheelOpen: false, hasCodes: true);
        Assert.True(activation.CanAcceptDigit(wheelOpen: false, inputPending: false));

        activation.PointerLeft(wheelOpen: false, inputPending: false);
        Assert.False(activation.CanAcceptDigit(wheelOpen: false, inputPending: false));
    }
}
