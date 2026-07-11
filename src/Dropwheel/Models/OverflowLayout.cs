namespace Dropwheel.Models;

/// <summary>How a wheel level lays out its tiles once they exceed a single rim. Below the
/// threshold every mode draws one ring, so a level with few targets looks unchanged.</summary>
public enum OverflowLayout
{
    /// <summary>The classic wheel: every tile stays on one ring, however many there are. No
    /// second ring ever appears. This is the default, so existing wheels are unchanged.</summary>
    None,

    /// <summary>Tiles split evenly across two equal-size rings.</summary>
    SplitBalanced,

    /// <summary>Inner ring stays the familiar single wheel up to its cap; only the surplus
    /// lands on a new outer band, staggered into the gaps. The second ring appears by need.</summary>
    OverflowBand,

    /// <summary>Tiles alternate between rings, the outer row offset into the gaps. The most
    /// compact layout — the smallest window.</summary>
    Petals,

    /// <summary>Tiles form radial pairs on shared spokes (inner + outer per column).</summary>
    Columns,
}
