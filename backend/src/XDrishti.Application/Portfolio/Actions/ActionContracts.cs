using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

// Request and response shapes of the Action-center API (ADR 0022). Money is in rupees; percentages (…Pct, Pp) are percent numbers (23.4 = 23.4 %, Pp = percentage points); dates are India calendar dates.

/// <summary>A profitable open lot that turns 12 months old soon (the <c>long_term_soon</c> rule): <c>LongTermOn</c> is the date it is 12 months old, <c>DaysLeft</c> the days until then, <c>GainPct</c> its unrealised gain in percent (null for a lot that cost nothing, for example bonus shares: the gain is then the whole value and has no percentage).</summary>
public sealed record LongTermLot(DateOnly AcquiredOn, long Qty, DateOnly LongTermOn, int DaysLeft, decimal? GainPct);

/// <summary>
/// The figures that made an item fire; which ones are set depends on the rule, the others are null. <b>below_cost</b>, <b>large_gain</b>: <c>PnlPct</c>, <c>Pnl</c>, <c>Invested</c>, <c>Value</c>, <c>ThresholdPct</c> (the band reached: 10 or 20 / 25).
/// <b>overweight</b>: <c>WeightPct</c> of the account's priced value, <c>ThresholdPct</c> (the limit), <c>Positions</c> (priced positions of the account), <c>Value</c>. <b>sector_concentration</b>: <c>Sector</c>, <c>SectorSharePct</c> of the account's priced value,
/// <c>ThresholdPct</c>, <c>SectorValue</c>, <c>Positions</c> (in the sector), <c>LargestSymbol</c>, <c>LargestHoldingId</c> and <c>LargestValue</c> of the largest holding in it. <b>long_term_soon</b>: <c>Lots</c>, <c>PnlPct</c>. <b>lagging_nifty</b>: <c>HeldDays</c>,
/// <c>XirrPct</c>, <c>BenchmarkXirrPct</c>, <c>GapPp</c> (holding minus benchmark, negative), <c>ThresholdPp</c>. <b>not_in_basket</b>: <c>Invested</c>, <c>Value</c> (null when unpriced).
/// </summary>
public sealed record ActionNumbers(
    decimal? PnlPct = null,
    decimal? Pnl = null,
    decimal? Invested = null,
    decimal? Value = null,
    decimal? ThresholdPct = null,
    decimal? WeightPct = null,
    int? Positions = null,
    string? Sector = null,
    decimal? SectorSharePct = null,
    decimal? SectorValue = null,
    string? LargestSymbol = null,
    Guid? LargestHoldingId = null,
    decimal? LargestValue = null,
    IReadOnlyList<LongTermLot>? Lots = null,
    int? HeldDays = null,
    decimal? XirrPct = null,
    decimal? BenchmarkXirrPct = null,
    decimal? GapPp = null,
    decimal? ThresholdPp = null);

/// <summary>
/// One Action-center item. <c>Id</c> is stable (<c>{rule}:{holdingId}</c>, or <c>sector_concentration:{accountId}:{12 hex}</c>) and safe in a URL path. <c>Rule</c> is one of <c>below_cost</c>, <c>large_gain</c>, <c>overweight</c>, <c>sector_concentration</c>,
/// <c>long_term_soon</c>, <c>lagging_nifty</c>, <c>not_in_basket</c>; <c>Severity</c> <c>high|medium|info</c>; <c>HoldingId</c>, <c>InstrumentId</c> and <c>Symbol</c> are null for an account-level item (concentration) and <c>InstrumentId</c> for a holding that is not in the
/// master list. <c>Title</c> and <c>Detail</c> are plain sentences; <c>Numbers</c> the figures behind them; <c>Suggestions</c> the buttons that make sense (<c>open|set_alert|add_to_basket|snooze|done</c>). <c>State</c> is <c>open|snoozed|done</c>;
/// <c>SnoozedUntil</c> the India date a snoozed item shows again. Severity, text and numbers are recomputed on every read.
/// </summary>
public sealed record ActionItem(
    string Id,
    string Rule,
    string Severity,
    Guid AccountId,
    string AccountName,
    Guid? HoldingId,
    Guid? InstrumentId,
    string? Symbol,
    string Title,
    string Detail,
    ActionNumbers Numbers,
    IReadOnlyList<string> Suggestions,
    string State,
    DateOnly? SnoozedUntil);

/// <summary>Rules that could not be evaluated: <c>Lagging</c> holdings held long enough for the lagging-Nifty rule but without a usable benchmark or XIRR.</summary>
public sealed record ActionUnavailable(int Lagging);

/// <summary>
/// What the Action center counts for the scope (before the status filter): items by state, and for the <i>open</i> items by rule (all seven rules, zeros included) and by severity (high, medium, info). <c>NoPrice</c> counts open holdings without a usable price
/// (no price-based rule can fire for them).
/// </summary>
public sealed record ActionCounts(
    int Open,
    int Snoozed,
    int Done,
    IReadOnlyDictionary<string, int> ByRule,
    IReadOnlyDictionary<string, int> BySeverity,
    ActionUnavailable Unavailable,
    int NoPrice);

/// <summary>The items of the status asked for, most important first, and the counts of the whole scope.</summary>
public sealed record ActionList(IReadOnlyList<ActionItem> Items, ActionCounts Counts);

/// <summary>The outcome of a decision: <c>State</c> <c>open|snoozed|done</c>, <c>SnoozedUntil</c>, and the item as it is now (null when the id is not produced by a rule now, which only a reopen can still ask about).</summary>
public sealed record ActionDecisionView(string Id, string State, DateOnly? SnoozedUntil, ActionItem? Item);

/// <summary>The thresholds in force with the documented defaults; <c>IsDefault</c> when they are the defaults; <c>UpdatedBy</c>/<c>UpdatedAt</c> null while none are stored.</summary>
public sealed record ActionThresholdsView(
    ActionThresholds Values,
    ActionThresholds Defaults,
    bool IsDefault,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt);

/// <summary>The thresholds to save: every value is required. <c>MinPositions</c>, <c>LagDays</c> and <c>LongTermWindowDays</c> must be whole numbers.</summary>
public sealed record ActionThresholdsInput(
    decimal? BelowCostPct,
    decimal? BelowCostHighPct,
    decimal? GainPct,
    decimal? OverweightFloorPct,
    decimal? OverweightMultiple,
    decimal? MinPositions,
    decimal? ConcentrationPct,
    decimal? LagPp,
    decimal? LagDays,
    decimal? LongTermWindowDays);
