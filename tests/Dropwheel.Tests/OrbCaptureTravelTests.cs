using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class OrbCaptureTravelTests
{
    private const double Home = 500;
    private const double MinTravel = 48;

    [Fact]
    public void A_release_on_the_orb_is_a_click_not_a_capture()
    {
        Assert.False(OverlayWindow.IsCapture(Home + 5, Home + 5, Home, Home, MinTravel));
    }

    [Fact]
    public void A_release_well_away_from_the_orb_is_a_capture()
    {
        Assert.True(OverlayWindow.IsCapture(Home + 200, Home - 120, Home, Home, MinTravel));
    }

    [Fact]
    public void The_boundary_distance_counts_as_a_capture()
    {
        Assert.True(OverlayWindow.IsCapture(Home + MinTravel, Home, Home, Home, MinTravel));
    }
}
