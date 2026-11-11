using XDrishti.Domain.Market.Actions;

namespace XDrishti.Domain.Market.History;

/// <summary>
/// The price factor of a bar by its India date against the instrument's seam (CF-04 Part B, decision 8 and review fix 3), instead of the single predicate of <see cref="AdjustmentFactors"/>. Bars <b>before the seam</b> are Dhan's and raw: they take the factors
/// of <b>every</b> applied action whose ex-date is after the bar's date, including those inside the CSV span, because the CSV basis contains them. Bars <b>at or after the seam</b> are the owner's CSV bars, already adjusted at source for actions up to
/// <c>csvAdjustedThrough</c>: they take only the actions whose ex-date is after both the bar and <c>csvAdjustedThrough</c>. As everywhere, an action's factor applies to bars strictly before its ex-date, so a bar on the ex-date takes none from it.
/// A Dhan bar after the CSV end follows the same rule as a CSV bar there (nothing lies between). Pure.
/// </summary>
public sealed class SourceAwareFactors
{
    private readonly AdjustmentFactors _factors;

    private readonly DateOnly _seam;

    private readonly DateOnly? _csvAdjustedThrough;

    public SourceAwareFactors(AdjustmentFactors factors, DateOnly seam, DateOnly? csvAdjustedThrough)
    {
        ArgumentNullException.ThrowIfNull(factors);
        _factors = factors;
        _seam = seam;
        _csvAdjustedThrough = csvAdjustedThrough;
    }

    /// <summary>The factor for a bar dated <paramref name="barDate"/> (1 when no action applies).</summary>
    public double FactorAt(DateOnly barDate)
    {
        var product = 1.0;
        foreach (var action in _factors.ActionsAfter(barDate))
        {
            if (barDate < _seam || _csvAdjustedThrough is not { } through || action.ExDate > through)
            {
                product *= (double)action.Factor;
            }
        }

        return product;
    }

    /// <summary>The price of a bar after its factor (rounded as the rest of the adjustment: 4 decimals).</summary>
    public double Price(double price, DateOnly barDate) => AdjustmentFactors.Price(price, FactorAt(barDate));

    /// <summary>The volume of a bar after its factor (divided by it, whole shares).</summary>
    public long Volume(long volume, DateOnly barDate) => AdjustmentFactors.Volume(volume, FactorAt(barDate));
}
