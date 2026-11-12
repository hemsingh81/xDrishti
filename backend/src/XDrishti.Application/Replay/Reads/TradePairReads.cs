using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The trades of a run in booking order, each with a link to the instrument's day bars where the symbol names one instrument.</summary>
public sealed record ListReplayTradesQuery(Guid RunId) : IQuery<Result<ReplayTradeListView>>;

internal sealed class ListReplayTradesHandler(IReplayReads reads, IListedInstruments instruments) : IQueryHandler<ListReplayTradesQuery, Result<ReplayTradeListView>>
{
    public async ValueTask<Result<ReplayTradeListView>> HandleAsync(ListReplayTradesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<ReplayTradeListView>(ReplayErrors.NotFound);
        }

        var trades = await reads.TradesAsync(run.Id, cancellationToken);
        var found = trades.Count == 0 ? new Dictionary<string, IReadOnlyList<Guid>>() : await instruments.FindAsync([.. trades.Select(t => t.Symbol).Distinct(StringComparer.Ordinal)], cancellationToken);
        var views = trades.Select(t => new ReplayBookTradeView(
            t.TradeNo, t.Symbol, t.Side, ReplayReadSupport.Strategy(t.CellKey), t.CellKey, t.Qty, t.EntrySession, t.EntryPrice, t.StopPrice, t.Swing, t.ExitSession, t.GrossPnl, t.Charges, t.NetPnl, t.RSim,
            found.TryGetValue(t.Symbol, out var ids) && ids.Count == 1 ? "/api/data/instruments/" + ids[0].ToString("D") + "/bars?timeframe=1D" : null)).ToList();
        return Result.Success(new ReplayTradeListView(run.Id, ReplayReadSupport.Completeness(run), ReplayReadSupport.CostsLabel(ReplayReadSupport.ParamsOf(run)), views));
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

        var a = await reads.SessionsAsync(frozen.Id, null, null, cancellationToken);
        var b = await reads.SessionsAsync(learning.Id, null, null, cancellationToken);
        var (fi, li) = (ReplayReadSupport.ListItem(frozen, a.Count == 0 ? null : a[^1]), ReplayReadSupport.ListItem(learning, b.Count == 0 ? null : b[^1]));
        if (!frozen.Complete || !learning.Complete)
        {
            return Result.Success(Empty(query.PairId, fi, li, "both runs must be complete to be compared (frozen " + frozen.State + ", learning " + learning.State + ")"));
        }

        var capital = ReplayReadSupport.ParamsOf(frozen)?.Capital ?? 0m;
        var compared = ReplayPairComparison.Compare([.. a.Select(s => new ReplayDayEquity(s.Session, s.Equity))], [.. b.Select(s => new ReplayDayEquity(s.Session, s.Equity))], capital);
        if (compared.IsFailure)
        {
            return Result.Failure<ReplayPairView>(compared.Error);
        }

        var r = compared.Value;
        return Result.Success(new ReplayPairView(query.PairId, fi, li, r.AnswerCode, null, r.Days, r.InformativeDays, r.MeanDailyDifference, r.ProbabilityLearningBetter, r.ProbabilityFrozenBetter, r.Low, r.High, r.Cut, r.HurtsBar, r.MinDays));
    }

    private static ReplayPairView Empty(Guid pair, ReplayRunListItem f, ReplayRunListItem l, string reason) => new(pair, f, l, null, reason, null, null, null, null, null, null, null, null, null, null);
}
