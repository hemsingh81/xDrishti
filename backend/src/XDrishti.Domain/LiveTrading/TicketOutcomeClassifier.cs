using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// Gives one approved live ticket its one outcome (spec P11-01, ADR 0149). Pure; a hand table in the tests.
/// <list type="bullet">
/// <item>Entry fills exist: <c>over_allocation</c> above the allocation; <c>partial</c> below it when an entry order shows a quantity that never filled (the shortfall is explained); otherwise <c>filled</c>. Flags: <c>size_below_allocation</c> (below the
/// configured percent with no explaining order), <c>chased_entry</c> (the VWAP worse than the plan entry by more than the tolerance), <c>unprotected</c>. Flags never change which ticket a fill belongs to.</item>
/// <item>No entry fill: a plan grade that is still pending gives <c>null</c> (nothing is written, the next settle retries) until the ticket has waited five sessions (the expired argument), then <c>unknown</c>; a replay that took no trade is <c>not_triggered</c>; a replay that cannot be graded
/// is <c>unknown</c>; a replay that traded is <c>missed_unknown</c> without order evidence (never <c>missed_no_order</c>), <c>rejected</c> when an entry order was rejected, <c>missed_unfilled</c> when an entry order never filled, and
/// <c>missed_no_order</c> when the owner placed none. Only the two missed outcomes carry the replay's R.</item>
/// </list>
/// </summary>
public static class TicketOutcomeClassifier
{
    public static TicketOutcome? Classify(
        TicketPlan plan,
        IReadOnlyList<LiveFill> entryFills,
        IReadOnlyList<LiveOrder>? entryOrders,
        PlanGradeFacts grade,
        string protection,
        MatchTolerances tolerances,
        bool expired,
        DateTimeOffset? firstEntryAt = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entryFills);
        ArgumentNullException.ThrowIfNull(grade);
        ArgumentNullException.ThrowIfNull(tolerances);
        var evidence = entryOrders is null ? OutcomeCodes.EvidenceUnavailable : OutcomeCodes.EvidencePresent;
        var ordered = entryOrders is { Count: > 0 } ? entryOrders.Max(o => o.Qty) : (long?)null;
        TicketOutcome Make(string outcome, long entryQty, IReadOnlyList<string> flags, double? missedR = null, string? reject = null) =>
            new(outcome, ordered, entryQty, firstEntryAt, flags, outcome is OutcomeCodes.MissedNoOrder or OutcomeCodes.MissedUnfilled ? missedR : null, grade.Status, evidence, protection, reject);

        var entryQty = entryFills.Sum(f => f.Qty);
        if (entryQty > 0)
        {
            return ClassifyFilled(plan, entryFills, entryOrders, entryQty, protection, tolerances, Make);
        }

        if (grade.Status == PlanGradeFacts.Pending)
        {
            return expired ? Make(OutcomeCodes.Unknown, 0, []) : null;
        }

        return grade.Status switch
        {
            PlanGradeFacts.NotTriggered => Make(OutcomeCodes.NotTriggered, 0, []),
            PlanGradeFacts.Graded when entryOrders is null => Make(OutcomeCodes.MissedUnknown, 0, []),
            PlanGradeFacts.Graded => ClassifyMissed(entryOrders, grade, Make),
            _ => Make(OutcomeCodes.Unknown, 0, []),
        };
    }

    private static TicketOutcome ClassifyMissed(IReadOnlyList<LiveOrder> orders, PlanGradeFacts grade, Func<string, long, IReadOnlyList<string>, double?, string?, TicketOutcome> make)
    {
        var rejected = orders.Where(o => o.Status == LiveOrderStatus.Rejected).OrderBy(o => o.CreateTime).FirstOrDefault();
        if (rejected is not null && orders.All(o => o.Status == LiveOrderStatus.Rejected))
        {
            return make(OutcomeCodes.Rejected, 0, [], null, rejected.RejectText);
        }

        var unfilled = orders.Any(o => o.Status != LiveOrderStatus.Rejected);
        return make(unfilled ? OutcomeCodes.MissedUnfilled : OutcomeCodes.MissedNoOrder, 0, [], grade.R, null);
    }

    private static TicketOutcome ClassifyFilled(
        TicketPlan plan,
        IReadOnlyList<LiveFill> fills,
        IReadOnlyList<LiveOrder>? orders,
        long entryQty,
        string protection,
        MatchTolerances tol,
        Func<string, long, IReadOnlyList<string>, double?, string?, TicketOutcome> make)
    {
        var flags = new List<string>();
        var explained = orders is not null && orders.Any(o => o.Status != LiveOrderStatus.Rejected && o.Qty > o.FilledQty);
        string outcome;
        if (entryQty > plan.AllocatedQty)
        {
            outcome = OutcomeCodes.OverAllocation;
            flags.Add(OutcomeCodes.FlagOverAllocation);
        }
        else if (entryQty < plan.AllocatedQty && explained)
        {
            outcome = OutcomeCodes.Partial;
        }
        else
        {
            outcome = OutcomeCodes.Filled;
            if (entryQty * 100m < plan.AllocatedQty * tol.SizeBelowPct)
            {
                flags.Add(OutcomeCodes.FlagSizeBelowAllocation);
            }
        }

        if (Chased(plan, fills, tol))
        {
            flags.Add(OutcomeCodes.FlagChasedEntry);
        }

        if (protection == OutcomeCodes.Unprotected)
        {
            flags.Add(OutcomeCodes.FlagUnprotected);
        }

        return make(outcome, entryQty, flags, null, null);
    }

    /// <summary>The VWAP is worse than the plan entry by more than the tolerance: R times the planned risk when the plan has a stop, else a percent of the entry. Exactly at the tolerance is not chased.</summary>
    private static bool Chased(TicketPlan plan, IReadOnlyList<LiveFill> fills, MatchTolerances tol)
    {
        if (plan.PlanEntry is not { } entry || entry <= 0m)
        {
            return false;
        }

        var qty = fills.Sum(f => f.Qty);
        var vwap = fills.Sum(f => f.Price * f.Qty) / qty;
        var worse = plan.Side == Side.Buy ? vwap - entry : entry - vwap;
        var limit = plan.PlanStop is { } stop && stop != entry ? Math.Abs(entry - stop) * tol.ChasedEntryR : entry * tol.ChasedEntryPct / 100m;
        return worse > limit;
    }
}
