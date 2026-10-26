namespace XDrishti.Domain.Paper;

/// <summary>
/// The derived figures of one paper account (spec P10-02). Rupees are decimals; a percentage or a figure that needs the capital is null when the capital is not known (BrokerFunds) or equity is not positive: unavailable, never 0.
/// </summary>
/// <param name="Gross">Realised rupees before charges.</param>
/// <param name="Charges">Realised charges of the stored sheet; null when no closed trade was charged.</param>
/// <param name="Result">Realised rupees after the stored sheet's charges where charged (gross where not); see <see cref="Basis"/>.</param>
/// <param name="Basis">What <see cref="Result"/> is after: gross, no charges / after the stored sheet / mixed.</param>
/// <param name="ClosedCount">Closed trades.</param>
/// <param name="OpenCount">Open positions.</param>
/// <param name="Equity">Capital plus the realised result; null without capital.</param>
/// <param name="OpenNotional">Entry value of the open positions.</param>
/// <param name="FundsAvailable">Equity less the open notional, at least 0 (no leverage); null without capital.</param>
/// <param name="OpenRisk">Rupees at risk to the initial stops.</param>
/// <param name="OpenRiskPct">Open risk as a percent of equity; null without positive equity.</param>
/// <param name="LastSessionResult">The realised result of the trades that closed on the last closed session (0 when none).</param>
/// <param name="LastSessionPct">That result as a signed percent of the equity at the start of that session; null without capital.</param>
/// <param name="MaxDrawdown">The largest fall of the running realised result from its peak (the start counts as a peak), rupees.</param>
/// <param name="MaxDrawdownPct">That fall as a percent of the capital plus the peak; null without capital.</param>
public sealed record LedgerFigures(
    decimal Gross,
    decimal? Charges,
    decimal Result,
    string Basis,
    int ClosedCount,
    int OpenCount,
    decimal? Equity,
    decimal OpenNotional,
    decimal? FundsAvailable,
    decimal OpenRisk,
    decimal? OpenRiskPct,
    decimal LastSessionResult,
    decimal? LastSessionPct,
    decimal MaxDrawdown,
    decimal? MaxDrawdownPct)
{
    /// <summary>The loss of the last closed session as a positive percent of equity; 0 when it was not a loss; null when not known.</summary>
    public decimal? DailyLossPct => LastSessionPct is { } p ? (p < 0 ? -p : 0m) : null;
}
