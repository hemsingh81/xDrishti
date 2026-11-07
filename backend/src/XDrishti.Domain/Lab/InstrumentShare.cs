namespace XDrishti.Domain.Lab;

/// <summary>
/// The largest share of the total R that one instrument holds (SC-S1, ADR 0222): the biggest instrument total over the overall total. When the overall total is not above 0 there is no share to speak of and the answer is 1.0 (all of it), the reading the year share
/// takes too, so a strategy that does not make money cannot pass the concentration limit by having a negative total. Pure.
/// </summary>
public static class InstrumentShare
{
    /// <param name="trades">The instrument and the R of each trade.</param>
    /// <returns>The share, 0 to 1 (1.0 when the total is not above 0); null with no trade.</returns>
    public static double? Max<TKey>(IReadOnlyList<(TKey Instrument, double R)> trades)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(trades);
        if (trades.Count == 0)
        {
            return null;
        }

        var total = trades.Sum(t => t.R);
        if (!(total > 0))
        {
            return 1.0;
        }

        // capped at 1: when the others lose, the largest instrument made more than the total, and "all of it" is the most a share can say
        return Math.Min(1.0, trades.GroupBy(t => t.Instrument).Max(g => g.Sum(t => t.R)) / total);
    }
}
