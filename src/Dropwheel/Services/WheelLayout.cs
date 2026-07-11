using Dropwheel.Models;

namespace Dropwheel.Services;

/// <summary>Position of one tile on the wheel: its angle (0 rad points right, tiles start at the
/// top, −π/2) and the radius of the ring it sits on, both measured from the wheel center.</summary>
public readonly record struct WheelCell(double Angle, double Radius);

/// <summary>Pure geometry for a wheel level. Given a layout mode and a tile count it returns one
/// cell per tile; the UI adds the center offset and the tile size. Below <see cref="SingleRingMax"/>
/// every mode draws one ring at <see cref="SingleRingRadius"/>, so a small level is unchanged. The
/// overflow always flows outward — a shorter inner circumference would cramp the drop target.</summary>
public static class WheelLayout
{
    /// <summary>Default main-ring capacity before overflow, and the lower/upper bounds the user's
    /// configured threshold is clamped to.</summary>
    public const int SingleRingMax = 9;
    public const int MinThreshold = 4;
    public const int MaxThreshold = 16;

    /// <summary>Radius of the single rim, matching the classic wheel.</summary>
    public const double SingleRingRadius = 170;

    private const double TileHalf = 32;
    private const double Margin = 24;
    private const double BaseWindow = 460;
    private const double Top = -Math.PI / 2;

    /// <summary>Clamps a user-entered threshold into the supported range.</summary>
    public static int ClampThreshold(int threshold) => Math.Clamp(threshold, MinThreshold, MaxThreshold);

    /// <summary>One cell per tile, in tile order. Empty for a non-positive count. The layout stays a
    /// single ring for mode None, or until the number of targets exceeds <paramref name="threshold"/>.
    /// <paramref name="reserved"/> is the count of always-present non-target tiles ("+" and, inside a
    /// group, "Back"); they add to the main-ring capacity so they never push a level into overflow on
    /// their own — the threshold counts real targets, matching what the user sees.</summary>
    public static WheelCell[] Compute(OverflowLayout mode, int count, int threshold = SingleRingMax, int reserved = 0)
    {
        if (count <= 0) return [];
        int cap = ClampThreshold(threshold) + reserved;
        if (mode == OverflowLayout.None || count <= cap) return Ring(count, SingleRingRadius, Top);
        return mode switch
        {
            OverflowLayout.SplitBalanced => Split(count),
            OverflowLayout.OverflowBand => Band(count, cap),
            OverflowLayout.Petals => Petals(count),
            OverflowLayout.Columns => Columns(count),
            _ => Band(count, cap),
        };
    }

    /// <summary>Square window side needed to hold the layout with a tile and margin around the
    /// outermost ring, never smaller than the classic window.</summary>
    public static double WindowSize(OverflowLayout mode, int count, int threshold = SingleRingMax, int reserved = 0)
    {
        double maxR = SingleRingRadius;
        foreach (var c in Compute(mode, count, threshold, reserved)) maxR = Math.Max(maxR, c.Radius);
        return Math.Max(BaseWindow, 2 * (maxR + TileHalf + Margin));
    }

    /// <summary>The largest window a mode can ever need, independent of tile count or threshold. The
    /// overlay window is fixed at this size for the whole session so it never resizes while the wheel
    /// opens or closes (a resizing, moving window is what makes the pointer logic glitch). None keeps
    /// the classic window; the overflow modes reserve room for their widest ring up front.</summary>
    public static double MaxWindowSize(OverflowLayout mode)
    {
        if (mode == OverflowLayout.None) return BaseWindow;
        // A count that always overflows yields the mode's widest ring regardless of the user's threshold.
        return WindowSize(mode, MaxThreshold + 8, MinThreshold);
    }

    /// <summary>The distinct ring radii the layout uses, innermost first (one or two values).</summary>
    public static IReadOnlyList<double> RingRadii(OverflowLayout mode, int count, int threshold = SingleRingMax, int reserved = 0)
    {
        var radii = new List<double>();
        foreach (var c in Compute(mode, count, threshold, reserved))
            if (!radii.Any(r => Math.Abs(r - c.Radius) < 0.5)) radii.Add(c.Radius);
        radii.Sort();
        return radii;
    }

    private static WheelCell[] Ring(int n, double r, double start)
    {
        var cells = new WheelCell[n];
        for (int i = 0; i < n; i++) cells[i] = new WheelCell(start + i * 2 * Math.PI / n, r);
        return cells;
    }

    private static WheelCell[] Split(int n)
    {
        int inner = n / 2, outer = n - inner;
        var ai = Ring(inner, 150, Top);
        var ao = Ring(outer, 250, Top);
        var cells = new WheelCell[n];
        Array.Copy(ai, 0, cells, 0, inner);
        Array.Copy(ao, 0, cells, inner, outer);
        return cells;
    }

    private static WheelCell[] Band(int n, int inner)
    {
        int outer = n - inner;
        var ai = Ring(inner, SingleRingRadius, Top);
        var ao = Ring(outer, 262, Top + Math.PI / outer); // half-step so the band sits in the gaps
        var cells = new WheelCell[n];
        Array.Copy(ai, 0, cells, 0, inner);
        Array.Copy(ao, 0, cells, inner, outer);
        return cells;
    }

    private static WheelCell[] Petals(int n)
    {
        int inner = (n + 1) / 2, outer = n - inner; // even tile indices go inner
        var ai = Ring(inner, 150, Top);
        var ao = Ring(outer, 236, outer > 0 ? Top + Math.PI / outer : Top);
        var cells = new WheelCell[n];
        int ci = 0, co = 0;
        for (int i = 0; i < n; i++)
            cells[i] = i % 2 == 0 ? ai[ci++] : ao[co++];
        return cells;
    }

    private static WheelCell[] Columns(int n)
    {
        int cols = (n + 1) / 2;
        var cells = new WheelCell[n];
        for (int i = 0; i < n; i++)
        {
            double angle = Top + (i / 2) * 2 * Math.PI / cols;
            cells[i] = new WheelCell(angle, i % 2 == 0 ? 150 : 250);
        }
        return cells;
    }
}
