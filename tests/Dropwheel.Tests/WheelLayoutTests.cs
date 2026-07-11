using Dropwheel.Models;
using Dropwheel.Services;

namespace Dropwheel.Tests;

/// <summary>Verifies the pure wheel layout: a level under the threshold stays a single ring for
/// every mode, overflow uses two rings that always sit outside the single rim, and each mode splits
/// its tiles the way its design intends. The window grows only when a second ring appears.</summary>
public sealed class WheelLayoutTests
{
    public static IEnumerable<object[]> AllModes =>
        Enum.GetValues<OverflowLayout>().Select(m => new object[] { m });

    public static IEnumerable<object[]> OverflowModes =>
        Enum.GetValues<OverflowLayout>()
            .Where(m => m != OverflowLayout.None)
            .Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Below_threshold_is_a_single_ring_for_every_mode(OverflowLayout mode)
    {
        for (int n = 1; n <= WheelLayout.SingleRingMax; n++)
        {
            var cells = WheelLayout.Compute(mode, n);
            Assert.Equal(n, cells.Length);
            Assert.All(cells, c => Assert.Equal(WheelLayout.SingleRingRadius, c.Radius, 3));
            Assert.Equal(WheelLayout.SingleRingRadius, WheelLayout.RingRadii(mode, n).Single(), 3);
        }
    }

    [Theory]
    [MemberData(nameof(OverflowModes))]
    public void Overflow_flows_outward_into_a_second_ring(OverflowLayout mode)
    {
        var cells = WheelLayout.Compute(mode, 14);
        Assert.Equal(14, cells.Length);
        var radii = WheelLayout.RingRadii(mode, 14);
        Assert.Equal(2, radii.Count);
        // The extra ring is always the outer one — a shorter inner circumference would cramp
        // the drop target — while the inner ring is only mildly tightened, never cramped.
        Assert.True(radii[1] > WheelLayout.SingleRingRadius,
            $"outer ring {radii[1]} should sit past the single rim");
        Assert.True(radii[0] >= 140, $"inner ring {radii[0]} should not be cramped");
    }

    [Theory]
    [MemberData(nameof(OverflowModes))]
    public void Window_grows_only_when_a_second_ring_appears(OverflowLayout mode)
    {
        Assert.Equal(460, WheelLayout.WindowSize(mode, WheelLayout.SingleRingMax), 3);
        Assert.True(WheelLayout.WindowSize(mode, WheelLayout.SingleRingMax + 3) > 460);
    }

    [Fact]
    public void None_never_overflows_however_many_targets()
    {
        var cells = WheelLayout.Compute(OverflowLayout.None, 20);
        Assert.Equal(20, cells.Length);
        Assert.All(cells, c => Assert.Equal(WheelLayout.SingleRingRadius, c.Radius, 3));
        Assert.Equal(460, WheelLayout.WindowSize(OverflowLayout.None, 20), 3);
        Assert.Single(WheelLayout.RingRadii(OverflowLayout.None, 20));
    }

    [Fact]
    public void Threshold_controls_when_the_extra_ring_appears()
    {
        const int threshold = 6;
        Assert.Single(WheelLayout.RingRadii(OverflowLayout.OverflowBand, threshold, threshold));
        Assert.Equal(2, WheelLayout.RingRadii(OverflowLayout.OverflowBand, threshold + 1, threshold).Count);
    }

    [Fact]
    public void OverflowBand_keeps_exactly_the_threshold_on_the_inner_ring()
    {
        const int threshold = 7;
        var cells = WheelLayout.Compute(OverflowLayout.OverflowBand, 13, threshold);
        int inner = cells.Count(c => Math.Abs(c.Radius - WheelLayout.SingleRingRadius) < 0.5);
        Assert.Equal(threshold, inner);
    }

    [Fact]
    public void Reserved_tiles_do_not_push_a_level_into_overflow()
    {
        // 12 targets + the "+" tile = 13 items; with threshold 12 and one reserved tile the level
        // must stay a single ring — the "+" is not a target and shouldn't trigger the extra ring.
        Assert.Single(WheelLayout.RingRadii(OverflowLayout.OverflowBand, 13, threshold: 12, reserved: 1));
        // a 13th target (14 items) does overflow, and the inner ring keeps the 12 targets + "+".
        var cells = WheelLayout.Compute(OverflowLayout.OverflowBand, 14, threshold: 12, reserved: 1);
        int inner = cells.Count(c => Math.Abs(c.Radius - WheelLayout.SingleRingRadius) < 0.5);
        Assert.Equal(13, inner);
    }

    [Fact]
    public void Threshold_is_clamped_into_range()
    {
        Assert.Equal(WheelLayout.MinThreshold, WheelLayout.ClampThreshold(1));
        Assert.Equal(WheelLayout.MaxThreshold, WheelLayout.ClampThreshold(99));
    }

    [Fact]
    public void Empty_and_nonpositive_counts_yield_no_cells()
    {
        Assert.Empty(WheelLayout.Compute(OverflowLayout.OverflowBand, 0));
        Assert.Empty(WheelLayout.Compute(OverflowLayout.Petals, -4));
    }

    [Fact]
    public void OverflowBand_keeps_the_inner_ring_full_at_the_classic_radius()
    {
        var cells = WheelLayout.Compute(OverflowLayout.OverflowBand, 13);
        int inner = cells.Count(c => Math.Abs(c.Radius - WheelLayout.SingleRingRadius) < 0.5);
        Assert.Equal(WheelLayout.SingleRingMax, inner);
    }

    [Fact]
    public void SplitBalanced_divides_tiles_evenly_between_the_two_rings()
    {
        var cells = WheelLayout.Compute(OverflowLayout.SplitBalanced, 12);
        var radii = WheelLayout.RingRadii(OverflowLayout.SplitBalanced, 12);
        int inner = cells.Count(c => Math.Abs(c.Radius - radii[0]) < 0.5);
        int outer = cells.Count(c => Math.Abs(c.Radius - radii[1]) < 0.5);
        Assert.Equal(6, inner);
        Assert.Equal(6, outer);
    }

    [Fact]
    public void Columns_places_pairs_on_the_same_angle()
    {
        var cells = WheelLayout.Compute(OverflowLayout.Columns, 12);
        // tiles 0/1, 2/3, … share a column (angle); the inner is even, the outer odd.
        for (int i = 0; i + 1 < cells.Length; i += 2)
            Assert.Equal(cells[i].Angle, cells[i + 1].Angle, 6);
    }

    [Fact]
    public void Petals_alternates_rings_by_tile_index()
    {
        var cells = WheelLayout.Compute(OverflowLayout.Petals, 12);
        var radii = WheelLayout.RingRadii(OverflowLayout.Petals, 12);
        for (int i = 0; i < cells.Length; i++)
        {
            double expected = i % 2 == 0 ? radii[0] : radii[1];
            Assert.Equal(expected, cells[i].Radius, 3);
        }
    }
}
