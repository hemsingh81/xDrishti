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
public sealed record NewPlanItem(int Rank, TicketGroup Group, string Symbol, string Side, string StrategyId, string StrategyVersionHash, string Trigger, string Stop, string Target, string Validity, IReadOnlyList<string> Reasons, PlanEvidence Evidence, IReadOnlyList<NewAllocation> Allocations, TicketLevels? Levels = null);

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
public sealed record PlanEvidence(string CellKey, int Trades, string DataRange, string PWinBasis, double? PWin, double? ConservativeExpectancyR, IReadOnlyList<string> Unavailable, string? AdvisoryNote, double? PWinLower = null);

/// <summary>Size for one Trading account. Only paper-mode allocations are accepted until the owner decides otherwise (P7-00 question 2).</summary>
/// <param name="AccountId">The broker account.</param>
/// <param name="Mode">The account's trading mode, "paper".</param>
/// <param name="Quantity">Shares or lots, above zero.</param>
public sealed record NewAllocation(Guid AccountId, string Mode, int Quantity);
