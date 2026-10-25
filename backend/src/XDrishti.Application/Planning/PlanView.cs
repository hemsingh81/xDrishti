using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>A plan as the API shows it (P7-03, ADR 0083): header, current state, the sentences it carries as data, and for one plan the tickets and the trail.</summary>
/// <param name="Id">The plan id.</param>
/// <param name="PlanKey">The idempotency key.</param>
/// <param name="ForDate">The session.</param>
/// <param name="ReviewCutoff">After this an owner approval or modification is refused; undecided tickets lapse.</param>
/// <param name="Version">Version within the date.</param>
/// <param name="State">draft, published, inreview, approved, rejected, lapsed, armed or expired.</param>
/// <param name="NextStates">The states the plan may move to.</param>
/// <param name="Basis">What the numbers rest on.</param>
/// <param name="Provisional">Whether cells were not yet paper-proven.</param>
/// <param name="Notice">Armed is not an order.</param>
/// <param name="Limits">The honest limits.</param>
/// <param name="CreatedAt">When built.</param>
/// <param name="CreatedBy">By whom.</param>
/// <param name="Primary">Primary tickets.</param>
/// <param name="Reserve">Reserve tickets.</param>
/// <param name="Rejected">Rejected candidates.</param>
/// <param name="Items">The candidates; empty in a list.</param>
/// <param name="History">The trail, oldest first; empty in a list.</param>
public sealed record PlanView(
    long Id,
    string PlanKey,
    DateOnly ForDate,
    DateTimeOffset ReviewCutoff,
    int Version,
    string State,
    IReadOnlyList<string> NextStates,
    string Basis,
    bool Provisional,
    string Notice,
    string Limits,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int Primary,
    int Reserve,
    int Rejected,
    IReadOnlyList<PlanItemView> Items,
    IReadOnlyList<PlanEventView> History)
{
    /// <summary>Builds the view from the stored row.</summary>
    public static PlanView Of(PlanRow row, IReadOnlyList<PlanItemView> items, IReadOnlyList<PlanEventView> history)
    {
        ArgumentNullException.ThrowIfNull(row);
        var p = row.Plan;
        return new PlanView(row.Id, p.PlanKey, p.ForDate, p.ReviewCutoff, p.Version, row.State.Code(), [.. PlanLifecycle.Next(row.State).Select(s => s.Code())], p.Basis, p.Provisional, p.Notice, p.Limits, p.CreatedAt, p.CreatedBy, row.Primary, row.Reserve, row.Rejected, items, history);
    }
}

/// <summary>One candidate of a plan.</summary>
/// <param name="Id">The item id.</param>
/// <param name="Rank">Order in the plan.</param>
/// <param name="Group">primary, reserve or rejected.</param>
/// <param name="State">The ticket state; null for a rejected candidate, which is not a ticket.</param>
/// <param name="NextStates">States the ticket may move to; empty for a rejected candidate.</param>
/// <param name="Symbol">Instrument.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">Strategy.</param>
/// <param name="StrategyVersionHash">Strategy version.</param>
/// <param name="Trigger">Conditions left to check live.</param>
/// <param name="Stop">Stop rule and level.</param>
/// <param name="Target">Target rule and level.</param>
/// <param name="Validity">Validity window.</param>
/// <param name="Reasons">Reason codes.</param>
/// <param name="Evidence">The evidence block.</param>
/// <param name="Levels">Indicative numbers as of the close of D (P7-19), or null for a plan stored without them or a ticket with no numeric level.</param>
/// <param name="Allocations">Size per account.</param>
public sealed record PlanItemView(
    long Id,
    int Rank,
    string Group,
    string? State,
    IReadOnlyList<string> NextStates,
    string Symbol,
    string Side,
    string StrategyId,
    string StrategyVersionHash,
    string Trigger,
    string Stop,
    string Target,
    string Validity,
    IReadOnlyList<string> Reasons,
    PlanEvidenceView Evidence,
    IReadOnlyList<PlanAllocationView> Allocations,
    TicketLevelsView? Levels = null)
{
    /// <summary>Builds the view from the stored row.</summary>
    public static PlanItemView Of(PlanItemRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var i = row.Item;
        var ticket = i.Group != TicketGroup.Rejected;
        var e = i.Evidence;
        return new PlanItemView(
            row.Id, i.Rank, i.Group.Code(), ticket ? row.State.Code() : null, ticket ? [.. TicketLifecycle.Next(row.State).Select(s => s.Code())] : [],
            i.Symbol, i.Side, i.StrategyId, i.StrategyVersionHash, i.Trigger, i.Stop, i.Target, i.Validity, i.Reasons,
            new PlanEvidenceView(e.CellKey, e.Trades, e.DataRange, e.PWinBasis, e.PWin, e.ConservativeExpectancyR, e.Unavailable, e.AdvisoryNote),
            [.. i.Allocations.Select(a => new PlanAllocationView(a.AccountId, a.Mode, a.Quantity))],
            i.Levels is { } l ? new TicketLevelsView(l.EntryRef, l.Session, l.Stop, l.Anchor?.Code(), l.Target, l.RewardRisk, TicketLevelsView.BasisText) : null);
    }
}

/// <summary>The evidence of a ticket, with its limits.</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Trades">Trades in the cell.</param>
/// <param name="DataRange">Data range and caveats.</param>
/// <param name="PWinBasis">What the probability rests on.</param>
/// <param name="PWin">Probability, or null.</param>
/// <param name="ConservativeExpectancyR">Conservative expectancy in R, or null.</param>
/// <param name="Unavailable">Missing inputs.</param>
/// <param name="AdvisoryNote">A model's figure, labelled advisory, or null.</param>
public sealed record PlanEvidenceView(string CellKey, int Trades, string DataRange, string PWinBasis, double? PWin, double? ConservativeExpectancyR, IReadOnlyList<string> Unavailable, string? AdvisoryNote);

/// <summary>Size for one account.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Mode">Its trading mode.</param>
/// <param name="Quantity">The quantity.</param>
public sealed record PlanAllocationView(Guid AccountId, string Mode, int Quantity);

/// <summary>One step of the trail.</summary>
/// <param name="Id">The row id.</param>
/// <param name="ItemId">The item, or null for the plan.</param>
/// <param name="From">State before.</param>
/// <param name="To">State after.</param>
/// <param name="Actor">Who.</param>
/// <param name="Reason">Why.</param>
/// <param name="ApprovalId">The approval row of an owner decision.</param>
/// <param name="At">When.</param>
public sealed record PlanEventView(long Id, long? ItemId, string From, string To, string Actor, string Reason, long? ApprovalId, DateTimeOffset At);

/// <summary>The indicative numbers of a ticket (spec P7-19, ADR 0108): rupees as of the close of the session, not tomorrow's price and not an order.</summary>
/// <param name="EntryRef">The close of the session.</param>
/// <param name="Session">The session D.</param>
/// <param name="Stop">The stop level, or null when the stop has no number.</param>
/// <param name="StopAnchor">"level" (a market level) or "close_of_d" (a distance from the close); null with no stop.</param>
/// <param name="Target">The target level of a fixed reward-to-risk exit, or null.</param>
/// <param name="RewardRisk">That reward-to-risk, or null.</param>
/// <param name="Basis">The pinned sentence on what the numbers are.</param>
public sealed record TicketLevelsView(decimal EntryRef, DateOnly Session, decimal? Stop, string? StopAnchor, decimal? Target, decimal? RewardRisk, string Basis)
{
    /// <summary>The fixed basis sentence.</summary>
    public const string BasisText = "indicative from the close of the session; not tomorrow's price";
}
