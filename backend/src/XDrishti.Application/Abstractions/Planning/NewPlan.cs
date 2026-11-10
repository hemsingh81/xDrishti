using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A plan to publish (spec P7-03, ADR 0083). Immutable once stored; a different input is a new version, never an edit.</summary>
/// <param name="PlanKey">The idempotency key: the same key published twice stores one plan.</param>
/// <param name="ForDate">The session the plan is for.</param>
/// <param name="ReviewCutoff">The review cutoff of the session, derived by the pipeline from the session calendar. An owner approval or modification at or after it is refused by the store and the database.</param>
/// <param name="Version">1 for the first plan of the date, then 2, 3 for a different input.</param>
/// <param name="InputHash">SHA-256 (hex) of the inputs the plan was built from.</param>
/// <param name="Basis">What the numbers rest on, for example "cell statistics only".</param>
/// <param name="Provisional">Whether the cells were not yet paper-proven (P7-00 question 7).</param>
/// <param name="CreatedBy">Who or what built it.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Items">The candidates: primary, reserve and rejected.</param>
public sealed record NewPlan(string PlanKey, DateOnly ForDate, DateTimeOffset ReviewCutoff, int Version, string InputHash, string Basis, bool Provisional, string CreatedBy, DateTimeOffset CreatedAt, IReadOnlyList<NewPlanItem> Items)
{
    /// <summary>The sentence that armed is not an order, stored with the plan.</summary>
    public string Notice { get; init; } = PlanWording.NoOrder;

    /// <summary>The honest-limits sentence, stored with the plan.</summary>
    public string Limits { get; init; } = PlanWording.HonestLimits;

    /// <summary>The smart-rule and data states as stored facts (P8-14); null for a plan stored before them or built without the smart rules.</summary>
    public PlanBasisFlags? BasisFlags { get; init; }
}

/// <summary>One candidate of a plan: levels as text rules, evidence as data, size per account.</summary>
/// <param name="Rank">1-based order within the plan, unique.</param>
/// <param name="Group">Primary, reserve or rejected.</param>
/// <param name="Symbol">The instrument symbol as of the plan.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">The stored strategy.</param>
/// <param name="StrategyVersionHash">The stored strategy version.</param>
/// <param name="Trigger">The conditions left to check live, as text.</param>
/// <param name="Stop">The stop rule and level.</param>
/// <param name="Target">The target rule and level.</param>
/// <param name="Validity">The validity window.</param>
/// <param name="Reasons">Reason codes; required for reserve and rejected.</param>
/// <param name="Evidence">The evidence block.</param>
/// <param name="Allocations">Size per Trading account.</param>
/// <param name="Levels">The indicative numbers as of the close of D (P7-19); null when the builder had none. Never read by approval, modification, grading or journal.</param>
/// <param name="Sizing">The down-only sizing note (BK-10): the scales, the capacity, the budget room and the settings version; null when no sizing setting or budget acted (the plan is then what it was without BK-10).</param>
public sealed record NewPlanItem(int Rank, TicketGroup Group, string Symbol, string Side, string StrategyId, string StrategyVersionHash, string Trigger, string Stop, string Target, string Validity, IReadOnlyList<string> Reasons, PlanEvidence Evidence, IReadOnlyList<NewAllocation> Allocations, TicketLevels? Levels = null, PlanSizingNote? Sizing = null);

/// <summary>The evidence on a ticket as data, with its honest limits.</summary>
/// <param name="CellKey">The cell the numbers come from.</param>
/// <param name="Trades">How many trades the cell has.</param>
/// <param name="DataRange">The data range and its caveats.</param>
/// <param name="PWinBasis">What the probability rests on.</param>
/// <param name="PWin">The probability, if any.</param>
/// <param name="ConservativeExpectancyR">The conservative expectancy in R, if any.</param>
/// <param name="Unavailable">Inputs that were missing; never a pass.</param>
/// <param name="AdvisoryNote">A model's figure shown beside, labelled advisory, or empty.</param>
/// <param name="PWinLower">The lower bound of the shrunk cell estimate of the win rate (P7-12: the only P(win) the auto-approval rule reads); null when not known.</param>
/// <param name="NEff">Independent n of the cell (CF-01), or null when not computed (AU-16: never read as a pass).</param>
/// <param name="StatsAsOf">The as-of date of the cell statistics, or null when there are none.</param>
public sealed record PlanEvidence(string CellKey, int Trades, string DataRange, string PWinBasis, double? PWin, double? ConservativeExpectancyR, IReadOnlyList<string> Unavailable, string? AdvisoryNote, double? PWinLower = null, double? NEff = null, DateOnly? StatsAsOf = null);

/// <summary>Size for one Trading account: paper, or manual live with its ramp scale (P11-04). The database repeats both rules.</summary>
/// <param name="AccountId">The broker account.</param>
/// <param name="Mode">The account's trading mode, "paper".</param>
/// <param name="Quantity">Shares or lots, above zero.</param>
/// <param name="RiskScale">For a manual-live row, the ramp scale the size was cut by (above 0, at most 1; spec P11-04); null for paper.</param>
/// <param name="RampEventId">For a manual-live row, the ramp event that scale comes from; null for paper.</param>
public sealed record NewAllocation(Guid AccountId, string Mode, int Quantity, double? RiskScale = null, long? RampEventId = null);

/// <summary>
/// What the down-only size scaling did to a ticket (spec BK-10, AC 10), stored as JSON on the plan item. Every figure is a factor at most 1 or a count; none is an amount of capital.
/// </summary>
/// <param name="VolScale">The volatility scale applied (floor to 1.0); null when not applied or unavailable.</param>
/// <param name="GateScale">The gate's scale, 0.75 in reduced mode, else 1.</param>
/// <param name="Factor">min(volatility scale, gate scale) times the budget ratio, at most 1.</param>
/// <param name="CapacityShares">The capacity cap in shares; null when off or unavailable.</param>
/// <param name="BudgetRoom">The room left in the tightest basket budget, percent of capital; null when there is no budget or it is unavailable.</param>
/// <param name="BudgetUsesPct">Share of that budget used after this ticket; null as above.</param>
/// <param name="SettingsVersion">The newest settings row id the scaling read (0 when none).</param>
/// <param name="Text">The same in words, for the ticket.</param>
public sealed record PlanSizingNote(double? VolScale, double GateScale, double Factor, long? CapacityShares, double? BudgetRoom, double? BudgetUsesPct, long SettingsVersion, string Text);
