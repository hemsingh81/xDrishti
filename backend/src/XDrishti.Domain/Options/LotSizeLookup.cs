namespace XDrishti.Domain.Options;

/// <summary>
/// The lot of an underlying's options in force on a date (spec P14-01 criterion 5): the row with the latest <c>effective_from</c> on or before the date; on the
/// same date a confirmed row wins, then the one recorded last. No such row is <see cref="LotInForce.Unavailable"/>, never a guess.
/// </summary>
public static class LotSizeLookup
{
    public static LotInForce InForce(IEnumerable<LotSizeEntry> rows, Guid underlyingId, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var row = rows.Where(r => r.UnderlyingId == underlyingId && r.EffectiveFrom <= date)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Confirmed).ThenByDescending(r => r.Id)
            .FirstOrDefault();
        return row is null ? LotInForce.Unavailable : new LotInForce(true, row.LotSize, row.Confirmed, row.EffectiveFrom, row.Source);
    }
}
