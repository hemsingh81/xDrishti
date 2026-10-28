using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Research;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The waterfall of one round trip from its stored execution (spec P11-02): plan result, entry slippage, exit difference, actual gross, and - only when every fill's charges are recorded - charges and actual net. A trip that did not close in the session, or whose plan
/// was not graded, is <c>unavailable</c> with the reason; it is never a zero. Read only.
/// </summary>
internal sealed class GetLiveWaterfallHandler(ILiveExecutionStore store) : IQueryHandler<LiveWaterfallQuery, Result<LiveWaterfallView>>
{
    public async ValueTask<Result<LiveWaterfallView>> HandleAsync(LiveWaterfallQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await store.FindExecutionAsync(query.EntryId, cancellationToken) is not { } stored)
        {
            return Result.Failure<LiveWaterfallView>(Error.NotFound(LiveErrors.NotFound, "No such execution"));
        }

        var e = stored.Row;
        string? why = e.PlanEntry is null || e.PlanExit is null
            ? "The plan grade of this ticket is not a graded trade, so there are no plan prices to compare with."
            : e.ExitVwap is null || e.ExitQty is null
                ? "No exit fill of this ticket was captured in the session: the position is still open or was left on a later session, which is not decomposed."
                : e.ExitQty != e.FillQty
                    ? "The exit quantity differs from the entry quantity (a partial exit or an addition), which is not decomposed."
                    : null;
        if (why is not null)
        {
            return Result.Success(new LiveWaterfallView(stored.Id, e.Symbol, e.ForDate, "unavailable", "unavailable", false, [], [why]));
        }

        var side = e.Side == "buy" ? Side.Buy : Side.Sell;
        var w = ExecutionWaterfall.Build(side, e.PlanEntry!.Value, e.PlanExit!.Value, e.PlanStop, e.FillVwap, e.ExitVwap!.Value, e.FillQty, e.LiveCharges, e.PlanChargesR);
        return Result.Success(new LiveWaterfallView(
            stored.Id, e.Symbol, e.ForDate, "value", w.Basis, w.Balanced,
            [.. w.Rows.Select(r => new LiveWaterfallRowView(r.Label, r.Op, LiveExecutionMapping.Number(r.R, "the plan grade has no stop"), r.Rupees))],
            w.Notes));
    }
}
