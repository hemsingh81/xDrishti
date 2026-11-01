namespace XDrishti.Domain.Options.Quality;

/// <summary>
/// Whether a series cannot exist on a date because of a dated rule (the epochs of P14-01). One rule is known: from the date of <c>weekly_index_one_per_exchange</c> only one weekly index per exchange is listed (Nifty), so the weekly series of Bank Nifty and
/// the other indices are out of era. A stock has no weekly series. The answer only labels an empty window; it never hides data that exists.
/// </summary>
public static class OutOfEra
{
    public const string WeeklyRule = "weekly_index_one_per_exchange";

    public const string KeptWeeklyIndex = "NSE:NIFTY";

    public static bool IsOutOfEra(string symbol, char expiryFlag, DateOnly date, IEnumerable<MarketEpoch> epochs)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(epochs);
        if (expiryFlag != 'W' || string.Equals(symbol, KeptWeeklyIndex, StringComparison.Ordinal))
        {
            return false;
        }

        return EpochLookup.InForce(epochs, date).Any(e => string.Equals(e.Code, WeeklyRule, StringComparison.Ordinal));
    }
}
