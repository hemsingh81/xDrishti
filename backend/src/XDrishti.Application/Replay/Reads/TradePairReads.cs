using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The trades of a run in booking order, each with a link to the instrument's day bars where the symbol names one instrument.</summary>
public sealed record ListReplayTradesQuery(Guid RunId) : IQuery<Result<ReplayTradeListView>>;

internal sealed class ListReplayTradesHandler(IReplayReads reads) : IQueryHandler<ListReplayTradesQuery, Result<ReplayTradeListView>>
{
    public async ValueTask<Result<ReplayTradeListView>> HandleAsync(ListReplayTradesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<ReplayTradeListView>(ReplayErrors.NotFound);
        }

        var trades = await reads.TradesAsync(run.Id, cancellationToken);
        var views = trades.Select(t => new ReplayBookTradeView(
            t.TradeNo, t.Symbol, t.Side, ReplayReadSupport.Strategy(t.CellKey), t.CellKey, t.Qty, t.EntrySession, t.EntryPrice, t.StopPrice, t.Swing, t.ExitSession, t.GrossPnl, t.Charges, t.NetPnl, t.RSim)).ToList();
        var p = ReplayReadSupport.ParamsOf(run);
        return Result.Success(new ReplayTradeListView(ReplayReadSupport.Labels(p, await reads.LabelsAsync(run.Id, cancellationToken)), run.Id, ReplayReadSupport.Completeness(run), ReplayReadSupport.CostsLabel(p), views));
    }
}

/// <summary>The frozen-versus-learning comparison of a pair (AC 9), computed on read from the two stored runs' equity series; nothing is stored for it.</summary>
public sealed record GetReplayPairQuery(Guid PairId) : IQuery<Result<ReplayPairView>>;

internal sealed class GetReplayPairHandler(IReplayReads reads) : IQueryHandler<GetReplayPairQuery, Result<ReplayPairView>>
{
    public async ValueTask<Result<ReplayPairView>> HandleAsync(GetReplayPairQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var runs = await reads.GetPairAsync(query.PairId, cancellationToken);
        if (runs.FirstOrDefault(r => r.Mode == "frozen") is not { } frozen || runs.FirstOrDefault(r => r.Mode == "learning") is not { } learning)
        {
            return Result.Failure<ReplayPairView>(Error.NotFound("replay.pair_not_found", "No such pair of replay runs."));
        }

        var a = await reads.EquityAsync(frozen.Id, null, null, cancellationToken);
        var b = await reads.EquityAsync(learning.Id, null, null, cancellationToken);
        var (fi, li) = (ReplayReadSupport.ListItem(frozen), ReplayReadSupport.ListItem(learning));
        var labels = ReplayReadSupport.Labels(ReplayReadSupport.ParamsOf(learning), fi.Hindsight.Select(l => l.Code).Concat(await reads.LabelsAsync(frozen.Id, cancellationToken)).Concat(await reads.LabelsAsync(learning.Id, cancellationToken)));
        if (!frozen.Complete || !learning.Complete)
        {
            return Result.Success(Empty(labels, query.PairId, fi, li, "both runs must be complete to be compared (frozen " + frozen.State + ", learning " + learning.State + ")"));
        }

        var capital = ReplayReadSupport.ParamsOf(frozen)?.Capital ?? 0m;
        var compared = ReplayPairComparison.Compare([.. a.Select(s => new ReplayDayEquity(s.Session, s.Equity))], [.. b.Select(s => new ReplayDayEquity(s.Session, s.Equity))], capital);
        if (compared.IsFailure)
        {
            return Result.Failure<ReplayPairView>(compared.Error);
        }

        var r = compared.Value;
        return Result.Success(new ReplayPairView(labels, query.PairId, fi, li, r.AnswerCode, null, r.Days, r.InformativeDays, r.MeanDailyDifference, r.ProbabilityLearningBetter, r.ProbabilityFrozenBetter, r.Low, r.High, r.Cut, r.HurtsBar, r.MinDays));
    }

    private static ReplayPairView Empty(IReadOnlyList<ReplayLabelView> labels, Guid pair, ReplayRunListItem f, ReplayRunListItem l, string reason) => new(labels, pair, f, l, null, reason, null, null, null, null, null, null, null, null, null, null);
}
