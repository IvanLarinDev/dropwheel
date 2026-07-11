using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Locks the proximity/suppression transition against the recurring "wheel opens by itself"
/// regressions: a press starting inside the zone must not auto-open, an approach from outside must,
/// release clears everything, and proximity-close only fires past the close radius.</summary>
public sealed class ProximityStateTests
{
    private const double OpenR2 = 150 * 150;
    private const double CloseR2 = 340 * 340;
    private const double Inside = 100 * 100;      // < openR2
    private const double OutsideOpen = 300 * 300; // > openR2, < closeR2
    private const double BeyondClose = 400 * 400; // > closeR2

    [Fact]
    public void A_press_that_begins_inside_the_open_radius_is_suppressed_and_does_not_start_a_beat()
    {
        var state = new ProximityState(PrevLeftDown: false, Suppressed: false, ProximityOpened: false);
        var (next, intent) = state.Step(leftDown: true, Inside, OpenR2, CloseR2, open: false);
        Assert.True(next.Suppressed);
        Assert.NotEqual(ProximityIntent.StartBeat, intent);
    }

    [Fact]
    public void An_approach_from_outside_that_crosses_inward_starts_a_beat()
    {
        var start = new ProximityState(false, false, false);
        // Frame 1: button goes down while still outside the open radius → nothing latched.
        var (mid, _) = start.Step(leftDown: true, OutsideOpen, OpenR2, CloseR2, open: false);
        Assert.False(mid.Suppressed);
        // Frame 2: still held, now inside → open.
        var (_, intent) = mid.Step(leftDown: true, Inside, OpenR2, CloseR2, open: false);
        Assert.Equal(ProximityIntent.StartBeat, intent);
    }

    [Fact]
    public void Releasing_clears_suppression_and_the_proximity_open_flag()
    {
        var state = new ProximityState(PrevLeftDown: true, Suppressed: true, ProximityOpened: true);
        var (next, intent) = state.Step(leftDown: false, Inside, OpenR2, CloseR2, open: true);
        Assert.False(next.Suppressed);
        Assert.False(next.ProximityOpened);
        Assert.NotEqual(ProximityIntent.StartBeat, intent);
    }

    [Fact]
    public void While_suppressed_and_held_crossing_inward_does_not_start_a_beat()
    {
        // The click-to-close-must-not-reopen guarantee: once suppressed, staying held and moving inward
        // stays suppressed until the button is released.
        var state = new ProximityState(PrevLeftDown: true, Suppressed: true, ProximityOpened: false);
        var (next, intent) = state.Step(leftDown: true, Inside, OpenR2, CloseR2, open: false);
        Assert.True(next.Suppressed);
        Assert.NotEqual(ProximityIntent.StartBeat, intent);
    }

    [Fact]
    public void A_proximity_opened_wheel_closes_only_beyond_the_close_radius()
    {
        var state = new ProximityState(PrevLeftDown: true, Suppressed: false, ProximityOpened: true);

        // Between the open and close radii the wheel stays open — no close, no reopen.
        var (_, hold) = state.Step(leftDown: true, OutsideOpen, OpenR2, CloseR2, open: true);
        Assert.NotEqual(ProximityIntent.Close, hold);

        // Past the close radius it closes and clears the proximity-opened flag.
        var (beyond, closeIntent) = state.Step(leftDown: true, BeyondClose, OpenR2, CloseR2, open: true);
        Assert.Equal(ProximityIntent.Close, closeIntent);
        Assert.False(beyond.ProximityOpened);

        // Having closed, a later inward frame does not re-issue a Close.
        var (_, stillIntent) = beyond.Step(leftDown: true, Inside, OpenR2, CloseR2, open: true);
        Assert.NotEqual(ProximityIntent.Close, stillIntent);
    }

    [Fact]
    public void A_wheel_not_opened_by_proximity_is_not_proximity_closed()
    {
        // If the user opened the wheel some other way (ProximityOpened false), drifting away must not
        // auto-close it.
        var state = new ProximityState(PrevLeftDown: true, Suppressed: false, ProximityOpened: false);
        var (_, intent) = state.Step(leftDown: true, BeyondClose, OpenR2, CloseR2, open: true);
        Assert.NotEqual(ProximityIntent.Close, intent);
    }
}
