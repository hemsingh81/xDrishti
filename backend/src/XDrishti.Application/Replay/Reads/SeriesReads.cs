using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The daily equity, drawdown, cash line and the benchmark rebased to the capital, between two dates (each optional).</summary>
public sealed record GetReplayEquityQuery(Guid RunId, DateOnly? From, DateOnly? To) : IQuery<Result<ReplayEquityView>>;

internal sealed class GetReplayEquityHandler(IReplayReads reads, IBasketBenchmark benchmark, IReplayFacts facts) : IQueryHandler<GetReplayEquityQuery, Result<ReplayEquityView>>
{
    /// <summary>How many calendar days before the first session the benchmark read starts, to find the close before it over a long holiday.</summary>
    private const int BaseLookbackDays = 10;

    public async ValueTask<Result<ReplayEquityView>> HandleAsync(GetReplayEquityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To)
        {
            return Result.Failure<ReplayEquityView>(Error.Validation(ReplayDriver.InvalidCode, "from must not be after to"));
        }

        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run || ReplayReadSupport.ParamsOf(run) is not { } p)
        {
            return Result.Failure<ReplayEquityView>(ReplayErrors.NotFound);
        }

        var all = await reads.EquityAsync(run.Id, null, query.To, cancellationToken); // from the start: the drawdown needs the peak before the window; no session record is read
        var trades = await reads.TradesAsync(run.Id, cancellationToken);
        var drawdowns = ReplayStats.Drawdowns(p.Capital, [.. all.Select(s => s.Equity)]);
        var rows = new List<(ReplayEquityRow Row, decimal Drawdown, decimal Cash)>();
        for (var i = 0; i < all.Count; i++)
        {
            if (query.From is null || all[i].Session >= query.From)
            {
                rows.Add((all[i], drawdowns[i], all[i].Equity - ReplayStats.OpenAt(trades, all[i].Session).Sum(t => t.EntryPrice * t.Qty)));
            }
        }

        string? symbol = null;
        string? reason = null;
        SortedDictionary<DateOnly, double>? closes = null;
        if (all.Count > 0)
        {
            var found = await benchmark.GetAsync(cancellationToken);
            if (found.Index is null)
            {
                reason = found.Reason ?? "no benchmark index";
            }
            else
            {
                symbol = found.Index.Symbol;
                try
                {
                    // read from the run start whatever `from` is, so the rebasing does not move with the window
                    var read = await facts.DayClosesAsync(found.Index.Id, all[0].Session.AddDays(-BaseLookbackDays), all[^1].Session, cancellationToken); // through the replay's hold-out clamp
                    closes = new SortedDictionary<DateOnly, double>(read.ToDictionary(b => b.Session, b => b.Close));
                }
                catch (ReplayHoldOutException)
                {
                    reason = "the benchmark could not be read inside the hold-out fence";
                }

                if (closes is { Count: 0 })
                {
                    reason = "the index has no day bars in the range";
                }
            }
        }

        var points = new List<ReplayEquityPoint>(rows.Count);
        double? last = null;
        double? basis = null;
        if (closes is { Count: > 0 })
        {
            // the base is the close before the first session (the capital is the equity before day 1); with no earlier bar, the first one
            var first = all[0].Session;
            basis = closes.Where(c => c.Key < first).Select(c => (double?)c.Value).LastOrDefault() ?? closes.First().Value;
            last = basis;
        }

        var cursor = 0;
        var ordered = closes?.ToList() ?? [];
        foreach (var (row, drawdown, cash) in rows)
        {
            while (cursor < ordered.Count && ordered[cursor].Key <= row.Session)
            {
                last = ordered[cursor++].Value; // carried forward over a day the index has no bar
            }

            decimal? rebased = basis is > 0 && last is { } l ? Math.Round(p.Capital * (decimal)(l / basis.Value), 2) : null;
            points.Add(new ReplayEquityPoint(row.Session, row.Equity, drawdown, cash, rebased));
        }

        return Result.Success(new ReplayEquityView(ReplayReadSupport.Labels(p, await reads.LabelsAsync(run.Id, cancellationToken)), run.Id, ReplayReadSupport.Completeness(run), ReplayReadSupport.CostsLabel(p), p.Capital, symbol, reason, !ReplayRunStates.IsActive(run.State), points));
    }
}

/// <summary>One row per stored session between two dates (each optional): taken, blocked by reason, the day's result, the equity.</summary>
public sealed record ListReplaySessionsQuery(Guid RunId, DateOnly? From, DateOnly? To) : IQuery<Result<ReplaySessionListView>>;

internal sealed class ListReplaySessionsHandler(IReplayReads reads) : IQueryHandler<ListReplaySessionsQuery, Result<ReplaySessionListView>>
{
    public async ValueTask<Result<ReplaySessionListView>> HandleAsync(ListReplaySessionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To)
        {
            return Result.Failure<ReplaySessionListView>(Error.Validation(ReplayDriver.InvalidCode, "from must not be after to"));
        }

        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<ReplaySessionListView>(ReplayErrors.NotFound);
        }

        var p = ReplayReadSupport.ParamsOf(run);
        var all = await reads.SessionsAsync(run.Id, null, query.To, cancellationToken);
        var rows = new List<ReplaySessionListItem>();
        var before = p?.Capital ?? 0m;
        foreach (var s in all)
        {
            var pnl = s.Equity - before;
            before = s.Equity;
            if (query.From is { } from && s.Session < from)
            {
                continue;
            }

            var record = ReplayJson.Read<ReplaySessionRecord>(s.RecordJson);
            var blocked = record is null
                ? new Dictionary<string, int>()
                : record.Blocked.GroupBy(b => b.Reason.Code()).ToDictionary(g => g.Key, g => g.Count());
            rows.Add(new ReplaySessionListItem(s.Session, record?.Candidates.Count ?? 0, record?.Taken.Count ?? 0, blocked, pnl, s.Equity, s.Drawdown, record?.OpenPositions ?? 0, ReplayReadSupport.Describe(record?.Labels ?? [])));
        }

        return Result.Success(new ReplaySessionListView(ReplayReadSupport.Labels(p, await reads.LabelsAsync(run.Id, cancellationToken)), run.Id, ReplayReadSupport.Completeness(run), ReplayReadSupport.CostsLabel(p), rows));
    }
}

/// <summary>The full record of one session.</summary>
public sealed record GetReplaySessionQuery(Guid RunId, DateOnly Session) : IQuery<Result<ReplaySessionView>>;

internal sealed class GetReplaySessionHandler(IReplayReads reads) : IQueryHandler<GetReplaySessionQuery, Result<ReplaySessionView>>
{
    public async ValueTask<Result<ReplaySessionView>> HandleAsync(GetReplaySessionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reads.GetRunAsync(query.RunId, cancellationToken) is not { } run)
        {
            return Result.Failure<ReplaySessionView>(ReplayErrors.NotFound);
        }

        if (await reads.SessionAsync(run.Id, query.Session, cancellationToken) is not { } row || ReplayJson.Read<ReplaySessionRecord>(row.RecordJson) is not { } r)
        {
            return Result.Failure<ReplaySessionView>(Error.NotFound("replay.session_not_found", "That session is not stored (yet)."));
        }

        var p = ReplayReadSupport.ParamsOf(run);
        var trades = await reads.TradesAsync(run.Id, cancellationToken);
        var book = ReplayStats.OpenAt(trades, row.Session)
            .Select(t => new ReplayOpenPositionView(t.Symbol, t.Side, t.Qty, t.EntrySession, t.EntryPrice, t.StopPrice, t.Swing))
            .ToList();
        static ReplayCandidateView C(ReplayCandidateRow c) => new(c.Symbol, c.Side, c.CellKey, c.Rank);
        return Result.Success(new ReplaySessionView(
            ReplayReadSupport.Labels(p, r.Labels),
            run.Id,
            row.Session,
            ReplayReadSupport.CostsLabel(p),
            [.. r.Candidates.Select(C)],
            [.. r.Ranked.Select(C)],
            [.. r.Taken.Select(C)],
            [.. r.Blocked.Select(b => new ReplayBlockedView(b.Symbol, b.CellKey, b.Reason.Code(), b.Detail))],
            [.. r.Fills.Select(f => new ReplayFillView(f.Symbol, f.Side, f.CellKey, f.Session, f.Status, f.Qty, f.EntryPrice, f.Detail))],
            r.Exits,
            row.Equity,
            row.Drawdown,
            r.OpenRiskPct,
            r.OpenPositions,
            r.Notes,
            !ReplayRunStates.IsActive(run.State),
            book));
    }
}
