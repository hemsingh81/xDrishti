using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Baskets side by side (BK-07 criterion 6): per basket only the index (m1b) and the relative strength (m8), not the full profile, and the member overlap of each pair. Each instrument is read once however many baskets share it. A basket whose index is not a value shows
/// its state and no rank; ranks print as an order only when every neighbouring gap is larger than its noise (<see cref="BasketRankBoard"/>). Reads only.
/// </summary>
internal sealed class GetBasketRankingHandler(
    ProfileWindowResolver windows,
    BasketMembershipLoader memberships,
    IBarQueries bars,
    BasketProfileSettings settings,
    ReportBenchmarkOptions benchmarkSettings,
    TimeProvider time,
    ILogger<GetBasketRankingHandler> logger) : IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>
{
    public async ValueTask<Result<BasketRankingView>> HandleAsync(GetBasketRankingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        var ids = ParseIds(query.Basket);
        if (ids.IsFailure)
        {
            return Result.Failure<BasketRankingView>(ids.Error);
        }

        var window = await windows.ResolveAsync(query.From, query.To, query.Index, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<BasketRankingView>(window.Error);
        }

        var win = window.Value;
        var loaded = new List<BasketMembership>();
        foreach (var id in ids.Value)
        {
            var one = await memberships.LoadAsync(id, win, null, cancellationToken);
            if (one.IsFailure)
            {
                return Result.Failure<BasketRankingView>(one.Error);
            }

            loaded.Add(one.Value);
        }

        var distinct = loaded.SelectMany(m => m.Instruments).Select(i => i.Id).Distinct().Count();
        if (distinct > benchmarkSettings.MaxDistinctInstruments)
        {
            return Result.Failure<BasketRankingView>(ReportErrors.Invalid($"The baskets hold {distinct} distinct instruments; at most {benchmarkSettings.MaxDistinctInstruments} are read in one call."));
        }

        var reader = new ProfileBars(bars);
        var options = settings.ToOptions(benchmarkSettings.DiscontinuityThreshold);
        IReadOnlyList<DailyClose> benchmark = win.Index is { } index ? (await reader.ReadAsync(index.Id, win.From, win.To, cancellationToken)).Closes : [];
        var inputs = new List<RankInput>(loaded.Count);
        var states = new Dictionary<Guid, IndexSeries>();
        foreach (var basket in loaded)
        {
            var members = new List<MemberSeries>(basket.Instruments.Count);
            foreach (var m in basket.Instruments)
            {
                members.Add(new MemberSeries(m.Symbol, (await reader.ReadAsync(m.Id, win.From, win.To, cancellationToken)).Closes, [], m.IsFno, m.IsAsmGsm));
            }

            var series = EqualWeightIndex.Build(members, basket.At, new ProfileWindow(win.From, win.To), options);
            states[basket.Id] = series;
            inputs.Add(new RankInput(basket.Id, series, RelativeStrength.Compute(series, benchmark, options.RelativeStrengthWindows).Returns));
        }

        var noBenchmark = BasketProfileMapper.NoBenchmark(win.Index?.Symbol, benchmark.Count > 0);
        var board = BasketRankBoard.Compute(inputs);
        var rows = new List<RankingRowView>(loaded.Count);
        for (var i = 0; i < loaded.Count; i++)
        {
            var row = board.Rows[i];
            MetricView Rs(int w) => BasketProfileMapper.Against(row.RelativeStrength.FirstOrDefault(r => r.Window == w)?.Return ?? Metric.Unavailable($"no {w}-session window"), noBenchmark);
            rows.Add(new RankingRowView(loaded[i].Id, loaded[i].Name, Rs(21), Rs(63), Rs(126), Rs(252), ReportViews.Metric(row.Rank), ReportViews.Metric(row.RiskAdjusted), ReportViews.Metric(states[loaded[i].Id].Status).State));
        }

        var overlap = new List<OverlapView>();
        for (var a = 0; a < loaded.Count; a++)
        {
            for (var b = a + 1; b < loaded.Count; b++)
            {
                overlap.Add(new OverlapView(loaded[a].Id, loaded[b].Id, ReportViews.Metric(MemberOverlap.Jaccard(loaded[a].At(win.To), loaded[b].At(win.To)))));
            }
        }

        var unavailable = new List<string>();
        if (noBenchmark is not null)
        {
            unavailable.Add("benchmark index: " + noBenchmark);
        }

        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.BasketRanking(logger, loaded.Count, reader.Reads, elapsed);
        string[] limits =
        [
            "Equal weight, rebalanced every day, price return only: dividends are not included; members as of the end of the window.",
            "Not separated means a gap is not larger than its standard error (tracking error of the pair times the square root of the days over 252): indicative, pairwise, uncorrected; it is not a significance test.",
            "A rank is on one relative strength window; a basket without a value there has no rank.",
            "The ranking describes the past; no figure here predicts, and no basket is labelled best.",
        ];
        return Result.Success(new BasketRankingView(
            win.View,
            new ProfileIndexView(win.Index?.Id, win.Index?.Symbol, win.IndexIsDefault),
            board.RankWindow,
            rows,
            overlap,
            board.Separated,
            new BenchmarkEvidenceView(BasketProfileMapper.Basis, distinct, win.View, limits, unavailable)));
    }

    private Result<List<Guid>> ParseIds(string? text)
    {
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<Guid>();
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
            {
                return Result.Failure<List<Guid>>(ReportErrors.Invalid($"'{part}' is not a basket id."));
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.Count < 2 || ids.Count > settings.MaxBaskets
            ? Result.Failure<List<Guid>>(ReportErrors.Invalid($"Name 2 to {settings.MaxBaskets} baskets (basket=<id>,<id>)."))
            : Result.Success(ids);
    }
}
