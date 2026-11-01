namespace XDrishti.Domain.Options;

/// <summary>
/// The market rule epochs in force on a date (spec P14-01 criterion 7): per code, the row with the latest <c>effective_from</c> on or before the date, ordered by
/// date then code. A rule whose first date is later is not in force; each row carries its <see cref="MarketEpoch.Confirmed"/> flag.
/// </summary>
public static class EpochLookup
{
    public static IReadOnlyList<MarketEpoch> InForce(IEnumerable<MarketEpoch> rows, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows.Where(r => r.EffectiveFrom <= date)
            .GroupBy(r => r.Code, StringComparer.Ordinal)
            .Select(g => g.MaxBy(r => r.EffectiveFrom)!)
            .OrderBy(r => r.EffectiveFrom).ThenBy(r => r.Code, StringComparer.Ordinal)];
    }
}
