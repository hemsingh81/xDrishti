namespace XDrishti.Domain.Learning;

/// <summary>
/// The events-per-variable cap on the features of a logistic model (CF-10 part 10b, ADR 0215, decision D2). Events are the smaller class, not the trades: 120 trades with 40 % wins hold 48 wins and 72 losses, so 48 events, and at 15 events per variable
/// the cap is <c>floor(48 / 15) = 3</c> features (4 at 10). The cap never exceeds the ladder's own limit (8). The rule of thumb (10 to 20, Peduzzi et al. 1996) is cited from memory and not fetched: UNVERIFIED. Pure.
/// </summary>
public static class EventsPerVariable
{
    /// <summary>The events: the smaller of the two classes.</summary>
    public static int Events(int wins, int losses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wins);
        ArgumentOutOfRangeException.ThrowIfNegative(losses);
        return Math.Min(wins, losses);
    }

    /// <summary>The most features the events allow: <c>min(floor(events / epv), maxFeatures)</c>.</summary>
    public static int Cap(int events, int epv, int maxFeatures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(events);
        ArgumentOutOfRangeException.ThrowIfLessThan(epv, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFeatures, 1);
        return Math.Min(events / epv, maxFeatures);
    }
}
