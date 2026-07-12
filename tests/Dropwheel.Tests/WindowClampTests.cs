using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Locks the pure window-edge clamp used when the fixed-size wheel window is positioned by the
/// hotkey or the saved orb spot: at least the 24-DIP margin of the window must stay on the virtual
/// screen so the large fixed window never clips off an edge. Pixel snapping stays in the UI (needs live
/// DPI); only the clamp math is unit-tested here.</summary>
public sealed class WindowClampTests
{
    private const double Half = 230; // a representative window half-size

    [Fact]
    public void A_center_well_inside_the_screen_is_unclamped()
    {
        // 960 is far from both edges → edge is exactly center - half.
        Assert.Equal(960 - Half, WheelLayout.ClampWindowEdge(960, Half, 0, 1920), 6);
    }

    [Fact]
    public void A_center_past_the_low_edge_clamps_to_the_margin()
    {
        // The 24-DIP margin must stay on screen: edge floor is screenLo - half + 24.
        Assert.Equal(0 - Half + 24, WheelLayout.ClampWindowEdge(-500, Half, 0, 1920), 6);
    }

    [Fact]
    public void A_center_past_the_high_edge_clamps_to_the_margin()
    {
        Assert.Equal(1920 - Half - 24, WheelLayout.ClampWindowEdge(9999, Half, 0, 1920), 6);
    }

    [Fact]
    public void The_low_and_high_clamps_are_symmetric_about_the_screen()
    {
        double lo = WheelLayout.ClampWindowEdge(-1e9, Half, 0, 1920);
        double hi = WheelLayout.ClampWindowEdge(1e9, Half, 0, 1920);
        Assert.Equal(0 - Half + 24, lo, 6);
        Assert.Equal(1920 - Half - 24, hi, 6);
    }

    [Fact]
    public void Works_on_a_virtual_screen_that_starts_at_a_negative_origin()
    {
        // A second monitor to the left gives a negative VirtualScreenLeft; the clamp must respect it.
        double lo = WheelLayout.ClampWindowEdge(-5000, Half, -1920, 1920);
        Assert.Equal(-1920 - Half + 24, lo, 6);
        double inside = WheelLayout.ClampWindowEdge(-960, Half, -1920, 1920);
        Assert.Equal(-960 - Half, inside, 6);
    }
}
