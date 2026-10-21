namespace XDrishti.SimOracle;

/// <summary>The named limits of ADR 0042 D15 (ten codes, in the ADR's order) and the one that concerns the gate (D17).</summary>
public static class OracleCostLimits
{
    /// <summary>The code of D17.</summary>
    public const string SwingGateBeforeCosting = "swing_gate_before_costing";

    /// <summary>The code of P4-04, D8.</summary>
    public const string PyramidCapitalCap = "pyramid_capital_cap";

    /// <summary>The eleven limits in order (the last is P4-04's).</summary>
    public static IReadOnlyList<OracleCostLimit> NotModelled { get; } =
    [
        new("fixed_capital", "every trade is sized from the same starting capital; trades of different instruments overlap"),
        new("no_margin_or_leverage", "intraday margin, leverage and funding are not modelled"),
        new("no_liquidity_cap", "no liquidity cap is applied"),
        new("one_order_per_leg", "each leg is one order for the brokerage cap"),
        new("cash_equity_only", "every instrument is charged at the sheet's cash-equity figures"),
        new("no_trading_tax", "tax on trading income and account-level fees are outside the charge"),
        new("rates_as_sheet", "the figures are the sheet's and exactly as right as the sheet"),
        new("dp_charge_is_flat", "the depository charge is one flat figure per delivery sale"),
        new("no_flat_order_fees", "a flat fee per order other than the brokerage cannot be expressed"),
        new(SwingGateBeforeCosting, "a swing position that costing skipped still blocks the later candidates the position gate refused"),
        new(PyramidCapitalCap, "the cap per trade is applied at the first entry's price; the adds of a pyramid can lift the notional above it"),
    ];

    /// <summary>
    /// D17: the position gate runs before the walk and costing after it, so a swing trade skipped by costing
    /// (quantity or cost) still counts as the strategy's position. A kept trade counts too.
    /// </summary>
    public static bool TradeCountsForTheGate(OracleCostSkip skip) =>
        skip is OracleCostSkip.None or OracleCostSkip.Quantity or OracleCostSkip.Cost;
}
