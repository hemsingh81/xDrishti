using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// Adjusts the stored (raw) day closes of an instrument for its applied corporate actions (ADR 0017 §5): a close dated before an ex-date is multiplied by the action's factor, so a lot of the quantity the broker
/// shows now has a continuous value across the ex-date (ADR 0020 §1). Pure. A close that is not a positive number is dropped (it cannot value anything).
/// </summary>
public static class AdjustedCloses
{
    public static IReadOnlyList<DailyClose> Adjust(IReadOnlyList<StoredClose> raw, AdjustmentFactors factors)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(factors);
        var result = new List<DailyClose>(raw.Count);
        foreach (var close in raw)
        {
            if (!double.IsFinite(close.Close) || close.Close <= 0.0)
            {
                continue;
            }

            var adjusted = AdjustmentFactors.Price(close.Close, factors.FactorAt(close.Date));
            if (adjusted > 0.0)
            {
                result.Add(new DailyClose(close.Date, (decimal)adjusted));
            }
        }

        result.Sort((a, b) => a.Date.CompareTo(b.Date));
        return result;
    }
}
