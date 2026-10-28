using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// Builds the stored execution of one entry (spec P11-02): the VWAP of the entry fills, the reference (the ticket's numeric level first, else the plan grade's simulated entry, else none), the slippage split into delay and execution, the bucket and the
/// participation, and the plan and exit prices the waterfall needs. Pure: the same inputs give the same row.
/// </summary>
internal static class LiveExecutionBuilder
{
    private const decimal CroreRupees = 10_000_000m;
    private const decimal MaxParticipationPct = 9999.999999m;

    public static NewLiveExecution Build(LiveExecutionInput input, BucketEdges edges)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(edges);
        var qty = input.EntryFills.Sum(f => f.Qty);
        var vwap = Math.Round(input.EntryFills.Sum(f => f.Qty * f.Price) / qty, 4, MidpointRounding.AwayFromZero);
        var graded = input.Grade is { Status: GradeStatus.Graded, EntryPrice: not null } g ? g : null;

        // Reference: the owner's numeric instruction first; its stop goes with it. Otherwise the plan grade's simulated entry and stop.
        var (kind, reference, stop) = input.TicketEntry is { } e && e > 0
            ? (ReferenceKind.TicketLevel, (decimal?)e, input.TicketStop ?? (graded?.StopPrice is { } gs ? (decimal?)(decimal)gs : null))
            : graded is not null
                ? (ReferenceKind.PlanGrade, (decimal?)(decimal)graded.EntryPrice!.Value, graded.StopPrice is { } s ? (decimal?)(decimal)s : null)
                : (ReferenceKind.Unavailable, null, null);
        decimal? risk = reference is { } r && stop is { } st && Math.Abs(r - st) > 0 ? Math.Abs(r - st) : null;
        var parts = reference is { } rf ? SlippageDecomposer.Decompose(input.Side, rf, input.Arrival, vwap, input.Tick, risk) : null;
        if (parts is null)
        {
            kind = ReferenceKind.Unavailable;
        }

        var value = qty * vwap;
        decimal? participation = input.AdvTurnoverCr is { } adv && adv > 0 ? Math.Min(Math.Round(value / (adv * CroreRupees) * 100m, 6, MidpointRounding.AwayFromZero), MaxParticipationPct) : null; // clamped to the column (numeric(10,6)) so an illiquid symbol cannot fail the session
        var first = input.EntryFills.Where(f => f.Time is not null).OrderBy(f => f.Time).Select(f => (DateTimeOffset?)new DateTimeOffset(f.Date.ToDateTime(f.Time!.Value), IndiaTime.Offset)).FirstOrDefault();
        int? delay = first is { } at && input.FirstOrderAt is { } ordered && at >= ordered ? (int)(at - ordered).TotalSeconds : null;

        // Exit and plan prices of the waterfall: only a graded plan gives them; the exit only when the owner left the position.
        decimal? planEntry = graded is null ? null : (decimal)graded.EntryPrice!.Value;
        decimal? planExit = graded?.ExitPrice is { } px ? (decimal)px : null;
        decimal? planStop = graded?.StopPrice is { } ps ? (decimal)ps : null;
        var exitQty = input.ExitFills.Sum(f => f.Qty);
        decimal? exitVwap = exitQty > 0 ? Math.Round(input.ExitFills.Sum(f => f.Qty * f.Price) / exitQty, 4, MidpointRounding.AwayFromZero) : null;
        var trip = input.EntryFills.Concat(input.ExitFills).ToList();
        decimal? liveCharges = exitQty > 0 && trip.All(f => input.Charges.ContainsKey(f.Id)) ? trip.Sum(f => input.Charges[f.Id]) : null;
        var product = input.EntryFills[0].Product;
        return new NewLiveExecution(
            input.OutcomeId,
            input.AccountId,
            input.ItemId,
            input.Date,
            input.Symbol,
            input.Side == Side.Buy ? "buy" : "sell",
            Style(product),
            product,
            edges.Classify(input.AdvTurnoverCr),
            input.AdvTurnoverCr is { } a ? Math.Round(a, 2, MidpointRounding.AwayFromZero) : null,
            participation,
            input.Tick,
            kind,
            parts is null ? null : reference,
            parts is null ? null : risk,
            input.Arrival,
            vwap,
            qty,
            parts is null ? null : (double)parts.Total,
            parts?.Delay is { } d ? (double)d : null,
            parts?.Execution is { } x ? (double)x : null,
            parts?.TotalTicks,
            parts?.TotalBps,
            parts?.TotalR,
            parts?.DelayR,
            parts?.ExecutionR,
            delay,
            planEntry,
            planExit,
            planStop,
            PlanChargesR(input.Side, graded, planEntry, planExit, planStop),
            exitVwap,
            exitQty > 0 ? exitQty : null,
            liveCharges);
    }

    /// <summary>The style word of a broker product: intraday for INTRADAY / MIS, delivery for the rest (at most 8 characters).</summary>
    public static string Style(string product) => product.ToUpperInvariant() is "INTRADAY" or "MIS" ? "intraday" : "delivery";

    /// <summary>The plan's own charges in R: the gap between the gross R on the plan prices and the stored net R when the grade is costed; 0 for a gross-only grade.</summary>
    private static double PlanChargesR(Side side, TicketGrade? grade, decimal? planEntry, decimal? planExit, decimal? planStop)
    {
        if (grade is null || grade.R is not { } net || !grade.CostBasis.StartsWith("net of", StringComparison.OrdinalIgnoreCase) || planEntry is not { } e || planExit is not { } x || planStop is not { } s || Math.Abs(e - s) <= 0)
        {
            return 0;
        }

        var d = side == Side.Buy ? 1m : -1m;
        var gross = (double)(d * (x - e) / Math.Abs(e - s));
        var difference = gross - net;
        return double.IsFinite(difference) ? Math.Max(0, difference) : 0;
    }
}
