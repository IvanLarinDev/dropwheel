using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OrbChargeTests
{
    private const double OpenR = 150;
    private const double OuterR = 300;

    [Fact]
    public void Beyond_the_outer_edge_there_is_no_charge()
    {
        Assert.Equal(0, OverlayWindow.ChargeFor(360, OpenR, OuterR));
    }

    [Fact]
    public void At_the_open_threshold_the_charge_is_full()
    {
        Assert.Equal(1, OverlayWindow.ChargeFor(OpenR, OpenR, OuterR));
    }

    [Fact]
    public void Inside_the_threshold_the_charge_stays_full()
    {
        Assert.Equal(1, OverlayWindow.ChargeFor(40, OpenR, OuterR));
    }

    [Fact]
    public void Halfway_across_the_zone_the_charge_is_a_half()
    {
        Assert.Equal(0.5, OverlayWindow.ChargeFor(225, OpenR, OuterR), 3);
    }
}
