using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.History;

/// <summary>
/// The writer's guard (decision 6, made date-based by CF-04d decision 8): of the bars a chunk returned, only those dated <b>before</b> the India date of the instrument's first CSV bar may be stored. The rest are counted as <c>beyond_seam</c> and dropped, so a stored CSV bar is never
/// replaced by Dhan's (the store lets a Dhan bar replace a CSV bar of the same minute) and no day mixes the two sources: the seam date is a pure-CSV day. The planner keeps the windows before the seam too; this is the second line. Pure.
/// </summary>
public static class SeamGuard
{
    public static (IReadOnlyList<Bar> Allowed, int BeyondSeam) Split(IReadOnlyList<Bar> bars, DateTimeOffset seam)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var seamDate = IndiaTime.Today(seam);
        var allowed = new List<Bar>(bars.Count);
        foreach (var bar in bars)
        {
            if (SessionClock.DateOf(bar.EpochSeconds) < seamDate)
            {
                allowed.Add(bar);
            }
        }

        return (allowed, bars.Count - allowed.Count);
    }
}
