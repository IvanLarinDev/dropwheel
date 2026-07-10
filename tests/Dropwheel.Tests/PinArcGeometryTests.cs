using Dropwheel.UI;

namespace Dropwheel.Tests;

public sealed class PinArcGeometryTests
{
    private const double Hub = 230;
    private const double LeftOffset = 38;
    private const double TopOffset = 40;
    private const double Lift = 46;

    private static (double X, double Y) Center(double left, double top) => (left + LeftOffset, top + TopOffset);

    private static double DistanceFromHub(double x, double y) => Math.Sqrt(Math.Pow(x - Hub, 2) + Math.Pow(y - Hub, 2));

    [Fact]
    public void The_arc_bows_away_from_the_hub_by_the_lift()
    {
        var from = (Left: Hub - LeftOffset, Top: Hub - TopOffset);
        var to = (Left: Hub - LeftOffset, Top: 60 - TopOffset);

        var mid = OverlayWindow.BowedMidpoint(from.Left, from.Top, to.Left, to.Top);

        var straight = Center((from.Left + to.Left) / 2, (from.Top + to.Top) / 2);
        var bowed = Center(mid.Left, mid.Top);
        Assert.Equal(DistanceFromHub(straight.X, straight.Y) + Lift, DistanceFromHub(bowed.X, bowed.Y), 3);
    }

    [Fact]
    public void A_drop_on_the_hub_still_bows_toward_the_destination()
    {
        var to = (Left: 380 - LeftOffset, Top: Hub - TopOffset);

        var mid = OverlayWindow.BowedMidpoint(Hub - LeftOffset, Hub - TopOffset, to.Left, to.Top);

        var bowed = Center(mid.Left, mid.Top);
        Assert.True(bowed.X > Hub, "the arc must lean toward the destination, not back through the hub");
        Assert.Equal(Hub, bowed.Y, 3);
    }

    [Fact]
    public void A_degenerate_flight_that_starts_and_ends_on_the_hub_is_left_alone()
    {
        var mid = OverlayWindow.BowedMidpoint(Hub - LeftOffset, Hub - TopOffset, Hub - LeftOffset, Hub - TopOffset);

        Assert.Equal(Hub - LeftOffset, mid.Left, 3);
        Assert.Equal(Hub - TopOffset, mid.Top, 3);
    }

    [Fact]
    public void The_arc_stays_on_the_line_between_the_hub_and_the_straight_midpoint()
    {
        var mid = OverlayWindow.BowedMidpoint(300 - LeftOffset, 300 - TopOffset, 120 - LeftOffset, 160 - TopOffset);

        var straight = Center((300 - LeftOffset + 120 - LeftOffset) / 2, (300 - TopOffset + 160 - TopOffset) / 2);
        var bowed = Center(mid.Left, mid.Top);
        var straightAngle = Math.Atan2(straight.Y - Hub, straight.X - Hub);
        var bowedAngle = Math.Atan2(bowed.Y - Hub, bowed.X - Hub);
        Assert.Equal(straightAngle, bowedAngle, 3);
    }
}
