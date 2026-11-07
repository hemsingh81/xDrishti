namespace XDrishti.Application.Lab;

/// <summary>
/// The liquidity tier and the capacity check of the out-of-sample trades of a Lab verdict (CF-05 D2 and D4, ADR 0223), the evidence of row 14 of the readiness scorecard. Report-only: nothing in the verdict reads it. Counts only; no trade is named.
/// A trade with no usable 20-day median is <see cref="TierUnavailable"/>. The capacity is held against 0.5 % of the 20-day median (measured) and 1 % of the fill minute's traded value, which is not read from day bars, so without a breach
/// every capacity reading is <see cref="CapacityUnavailable"/> and none is a pass; <see cref="OrdersOverEntryDayShare"/> is information only.
/// </summary>
/// <param name="Trades">The out-of-sample trades judged (the sum of the four tier counts).</param>
/// <param name="TierA">Trades in tier A.</param>
/// <param name="TierB">Trades in tier B.</param>
/// <param name="TierC">Trades in tier C (excluded from edge claims, counted here).</param>
/// <param name="TierUnavailable">Trades with no usable 20-day median traded value (or whose bars could not be read).</param>
/// <param name="CapacityBreaches">Trades whose order is over a cap that was measured (the 20-day median share).</param>
/// <param name="CapacityUnavailable">Trades whose capacity could not be fully judged (the fill minute is not read, a traded value or the order value is missing, or the bars could not be read) and that are not already a breach.</param>
/// <param name="Settings">The text of the tier and capacity settings that produced the counts.</param>
/// <param name="SettingsHash">SHA-256 of <paramref name="Settings"/>.</param>
/// <param name="Basis">What is measured and what is not read.</param>
/// <param name="OrdersOverEntryDayShare">Information only: orders over the capacity share of the entry day's traded value (about 375 times looser than the fill minute's); it can never make a pass. Null in a block produced before it.</param>
public sealed record CostStressTiersView(
    int Trades,
    int TierA,
    int TierB,
    int TierC,
    int TierUnavailable,
    int CapacityBreaches,
    int CapacityUnavailable,
    string Settings,
    string SettingsHash,
    string Basis,
    int? OrdersOverEntryDayShare = null);
