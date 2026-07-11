using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Locks the copy/move and routing precedence that every file drop goes through.</summary>
public sealed class DropDispatchTests
{
    [Fact]
    public void Ctrl_forces_copy_over_everything()
    {
        Assert.Equal(DropAction.Copy,
            DropDispatch.ResolveAction(ctrl: true, shift: true, DropAction.Move, DropAction.Move));
    }

    [Fact]
    public void Shift_forces_move_when_ctrl_is_not_held()
    {
        Assert.Equal(DropAction.Move,
            DropDispatch.ResolveAction(ctrl: false, shift: true, DropAction.Copy, DropAction.Copy));
    }

    [Fact]
    public void Target_override_wins_over_global_when_no_modifier()
    {
        Assert.Equal(DropAction.Move,
            DropDispatch.ResolveAction(ctrl: false, shift: false, DropAction.Move, DropAction.Copy));
    }

    [Fact]
    public void Global_action_applies_when_no_modifier_and_no_override()
    {
        Assert.Equal(DropAction.Copy,
            DropDispatch.ResolveAction(ctrl: false, shift: false, DropAction.Inherit, DropAction.Copy));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void Virtual_files_and_text_always_copy_regardless_of_modifiers(bool ctrl, bool shift)
    {
        Assert.Equal(DropAction.Copy,
            DropDispatch.EffectiveAction(copyOnly: true, ctrl, shift, DropAction.Move, DropAction.Move));
    }

    [Fact]
    public void EffectiveAction_defers_to_resolve_for_real_files()
    {
        Assert.Equal(DropAction.Move,
            DropDispatch.EffectiveAction(copyOnly: false, ctrl: false, shift: true, DropAction.Inherit, DropAction.Copy));
    }

    [Fact]
    public void Sorter_routes_before_run_before_copy()
    {
        Assert.Equal(FileDropRoute.Sort, DropDispatch.ClassifyFileDrop(isSorter: true, isRunTarget: true));
        Assert.Equal(FileDropRoute.Run, DropDispatch.ClassifyFileDrop(isSorter: false, isRunTarget: true));
        Assert.Equal(FileDropRoute.CopyMove, DropDispatch.ClassifyFileDrop(isSorter: false, isRunTarget: false));
    }
}
