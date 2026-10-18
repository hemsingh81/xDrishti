namespace XDrishti.Domain.Research;

/// <summary>What the costing and the sizing do not model (ADR 0042, D15 and D17). The run banner lists them next to <see cref="FillLimits.NotModelled"/>. The texts name no figure about performance.</summary>
public static class CostLimits
{
    public static IReadOnlyList<FillLimit> NotModelled { get; } =
    [
        new("fixed_capital", "Every trade is sized from the same starting capital: there is no compounding and no equity curve; trades of different instruments overlap and their combined exposure is not capped."),
        new("no_margin_or_leverage", "Intraday margin, leverage, funding and interest are not modelled; the cap per trade is a cap on notional, which the filled entry may exceed by a few ticks."),
        new("no_liquidity_cap", "The liquidity cap of doc 08 is not applied: the quantity does not depend on the volume traded."),
        new("one_order_per_leg", "Each leg is one order for the brokerage cap; partial fills and order splitting are not modelled."),
        new("cash_equity_only", "Every instrument is charged at the sheet's NSE cash-equity figures; ETFs, other series, BSE, futures and options are not told apart."),
        new("no_trading_tax", "Tax on trading income and account-level fees such as annual maintenance are outside the charge."),
        new("rates_as_sheet", "The figures are the sheet's and exactly as right as the sheet; an unchecked sheet is labelled so."),
        new("dp_charge_is_flat", "The depository charge is one flat figure per delivery sale; the depository's own lines vary and some are 0."),
        new("no_flat_order_fees", "A flat fee per order other than the brokerage, such as an auto square-off fee, cannot be expressed; 'other' is a share of turnover."),
        new("swing_gate_before_costing", "A swing position that costing skipped still blocks the later candidates that the position gate refused: the gate runs before costing."),
    ];
}
