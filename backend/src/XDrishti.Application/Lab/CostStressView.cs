namespace XDrishti.Application.Lab;

/// <summary>One row of the cost stress (CF-05): the multiplier, the out-of-sample trades restated, the mean stressed net R, the floor and its comparison, and the reading (<c>pass</c>, <c>fail</c> or <c>unavailable</c>, which is never a pass) with its reason.</summary>
public sealed record CostStressRowView(double Multiplier, int Trades, double? ExpectancyR, double Threshold, string Comparison, string Status, string Reason);

/// <summary>The fill-one-minute-later test of CF-05, per signal: a signal that no longer fills counts as 0 R. The means and the share are null when the base is not above 0 (the reading then says not evaluable).</summary>
public sealed record CostStressDelayView(int Minutes, int Signals, int DelayedFilled, double? BaseMeanR, double? DelayedMeanR, double? Retention, double Required, string Status, string Reason, string Note);

/// <summary>
/// The cost stress and the delay test of a Lab verdict (CF-05, ADR 0206), shown beside PASS/MARGINAL/FAIL: report-only unless the owner turns <c>Enforce</c> on, then a miss caps the outcome at MARGINAL and never raises it. Every settings figure used is in
/// <see cref="Settings"/> and its hash in <see cref="SettingsHash"/>. <see cref="Tradable"/> is the worst of the three multipliers. <see cref="CostsChecked"/> is false until <c>cost-sheet check</c> has recorded a bill check, and the rows are then unavailable.
/// </summary>
/// <param name="Mode">The mode when it was computed.</param>
/// <param name="Enforce">Whether a miss capped the outcome.</param>
/// <param name="Settings">The text of the settings that produced the figures.</param>
/// <param name="SettingsHash">SHA-256 of <paramref name="Settings"/>.</param>
/// <param name="CostsChecked">The charge sheet is checked against a bill.</param>
/// <param name="SheetId">The charge sheet the trades were costed with.</param>
/// <param name="Rows">1x, the middle and the high multiplier.</param>
/// <param name="Tradable">The worst of the rows: <c>pass</c>, <c>fail</c> or <c>unavailable</c>.</param>
/// <param name="Delay">The delay test; null when the run did not take it (a full test, not a robust one).</param>
/// <param name="Capped">True when the outcome was capped at MARGINAL by an enforced stress gate.</param>
/// <param name="Formula">The stress formula.</param>
/// <param name="Limit">The first-order limit of the formula.</param>
/// <param name="SheetRatesSha256">The hash of the sheet's rates the run was costed with, so a stored verdict says which numbers were checked.</param>
public sealed record CostStressView(
    string Mode,
    bool Enforce,
    string Settings,
    string SettingsHash,
    bool CostsChecked,
    string? SheetId,
    IReadOnlyList<CostStressRowView> Rows,
    string Tradable,
    CostStressDelayView? Delay,
    bool Capped,
    string Formula,
    string Limit,
    string? SheetRatesSha256 = null);
