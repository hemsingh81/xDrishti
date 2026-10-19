using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>The quantity of a simulated trade (ADR 0042, D11). Pure, decimal, and never a division by zero.</summary>
public static class Sizing
{
    /// <summary>
    /// <c>floor(min(riskAmount / risk, maxCapital / entry))</c> with <c>riskAmount = capital × risk% / 100</c> and <c>maxCapital = capital × cap% / 100</c>: whole shares (lot size 1), rounded down so that the risk taken never exceeds the budget.
    /// 0 means the trade is skipped (the caller counts it). <paramref name="entry"/> is the exact entry and <paramref name="risk"/> the distance from it to the stop per share (the unit of R); the filled price plays no part.
    /// </summary>
    /// <returns>The quantity, or <c>costs.sizing_input_invalid</c> when the capital, the entry or the risk is not above 0.</returns>
    /// <exception cref="ArgumentNullException">The profile is null.</exception>
    public static Result<long> Quantity(decimal capital, SizingProfile profile, decimal entry, decimal risk)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (capital <= 0)
        {
            return Result.Failure<long>(CostErrors.SizingInputInvalid("capital"));
        }

        if (entry <= 0)
        {
            return Result.Failure<long>(CostErrors.SizingInputInvalid("entry"));
        }

        if (risk <= 0)
        {
            return Result.Failure<long>(CostErrors.SizingInputInvalid("risk"));
        }

        var riskAmount = capital * profile.RiskPerTradePct / 100m;
        var maxCapital = capital * profile.MaxCapitalPerTradePct / 100m;
        var shares = Math.Floor(Math.Min(riskAmount / risk, maxCapital / entry));
        return Result.Success((long)shares);
    }
}
