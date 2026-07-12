namespace Dropwheel.Services;

/// <summary>Decides whether a one-off hint should be shown again, based on how many times it already
/// has been. Pure over the counts dictionary so the cap behaviour is unit-tested; the UI keeps the
/// dictionary in config and does the actual toast.</summary>
public static class HintPolicy
{
    /// <summary>Records one more show of the hint and returns whether this show is allowed (still under
    /// its cap). Mutates <paramref name="shows"/>; a maxed-out hint is left unchanged and returns false.</summary>
    public static bool RecordAndAllow(Dictionary<string, int> shows, string id, int max)
    {
        int seen = shows.TryGetValue(id, out var v) ? v : 0;
        if (seen >= max) return false;
        shows[id] = seen + 1;
        return true;
    }
}
