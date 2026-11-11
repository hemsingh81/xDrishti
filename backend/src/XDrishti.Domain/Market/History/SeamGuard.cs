using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.History;

/// <summary>
/// The writer's guard (decision 6): of the bars a chunk returned, only those that start before the instrument's first CSV bar may be stored. The rest are counted as <c>beyond_seam</c> and dropped, so a stored CSV bar is never replaced by Dhan's (the store lets a Dhan bar
/// replace a CSV bar of the same minute). The planner keeps the windows before the seam too; this is the second line. Pure.
/// </summary>
public static class SeamGuard
{
    public static (IReadOnlyList<Bar> Allowed, int BeyondSeam) Split(IReadOnlyList<Bar> bars, DateTimeOffset seam)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var seamEpoch = seam.ToUnixTimeSeconds();
        var allowed = new List<Bar>(bars.Count);
        foreach (var bar in bars)
        {
            if (bar.EpochSeconds < seamEpoch)
            {
                allowed.Add(bar);
            }
        }

        return (allowed, bars.Count - allowed.Count);
    }
}
