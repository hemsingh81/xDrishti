using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>The latest rule epoch of protocol O (P14-09): from 2025-09-01, epoch <c>nifty_expiry_tuesday</c>. The stored epoch row gives its date when present; the spec's date is the default (and the same default the expiry table uses).</summary>
public static class ExpressionEpochs
{
    /// <summary>The code of the latest epoch.</summary>
    public const string LatestCode = ExpiryRules.TuesdayEpoch;

    /// <summary>The spec's date.</summary>
    public static DateOnly DefaultLatestFrom { get; } = new(2025, 9, 1);

    /// <summary>The first day of the latest epoch.</summary>
    public static DateOnly LatestFrom(IEnumerable<MarketEpoch> epochs)
    {
        ArgumentNullException.ThrowIfNull(epochs);
        return epochs.FirstOrDefault(e => e.Code == LatestCode)?.EffectiveFrom ?? DefaultLatestFrom;
    }
}
