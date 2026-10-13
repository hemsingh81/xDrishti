using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// Day change against the previous close (design doc 12 §3, ADR 0019 §4). Pure: no I/O, no clock. Per holding: <c>qty × (last − previousClose)</c> and <c>(last − previousClose) / previousClose</c>; it is unavailable when the
/// previous close is missing or not positive, when the holding has no usable price or is closed. Per group and in total: the sum of the amounts, with the percentage on the sum of <c>qty × previousClose</c> of the
/// included holdings (not an average of percentages); a holding left out is counted in <see cref="GroupDayChange.Excluded"/> and never taken as zero. Amounts and percentages are given to four decimals.
/// </summary>
public static class DayChangeCalculator
{
    private const int Decimals = 4;

    /// <summary>The day change of one open holding; null when it is unavailable.</summary>
    public static HoldingDayChange? ForHolding(HoldingData holding)
    {
        ArgumentNullException.ThrowIfNull(holding);
        if (!Counts(holding))
        {
            return null;
        }

        var previous = holding.PreviousClose!.Value;
        return new HoldingDayChange(Round(holding.Qty * (holding.LastPrice - previous)), Round((holding.LastPrice - previous) / previous * 100m));
    }

    /// <summary>The day change of the open holdings among <paramref name="holdings"/> (closed ones are ignored, not counted as excluded).</summary>
    public static GroupDayChange ForGroup(IEnumerable<HoldingData> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        var amount = 0m;
        var previousValue = 0m;
        var included = 0;
        var excluded = 0;
        foreach (var holding in holdings.Where(h => h.IsOpen))
        {
            if (!Counts(holding))
            {
                excluded++;
                continue;
            }

            included++;
            amount += holding.Qty * (holding.LastPrice - holding.PreviousClose!.Value);
            previousValue += holding.Qty * holding.PreviousClose.Value;
        }

        return included == 0
            ? new GroupDayChange(null, null, 0, excluded)
            : new GroupDayChange(Round(amount), previousValue > 0m ? Round(amount / previousValue * 100m) : null, included, excluded);
    }

    private static bool Counts(HoldingData holding) =>
        holding.IsOpen && holding.Qty > 0 && holding.LastPrice > 0m && holding.PreviousClose is > 0m;

    private static decimal Round(decimal value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
}
