using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// What holding each basket, and the benchmark index, would have done over the same window (P8-07, ADR 0102): members as of one stated date, the stored daily bars read once per instrument, and the pure <see cref="BuyAndHold"/> rule. Reads only;
/// the owner's benchmark choice is the portfolio's, tracked nowhere here.
/// </summary>
internal sealed class GetReportBenchmarksHandler(
    IBasketQueries baskets,
    IBarQueries bars,
    IPortfolioBenchmark benchmark,
    IReportTradeSource source,
    ReportBenchmarkOptions settings,
    TimeProvider time,
    ILogger<GetReportBenchmarksHandler> logger) : IQueryHandler<GetReportBenchmarksQuery, Result<ReportBenchmarksView>>
{
    private const string Basis = "price return, dividends not included";

    private static readonly string[] Limits =
    [
        "Equal weight, bought at the first close of the window and held, no rebalancing; price return only, dividends are not included.",
        "Members are as of one date, not as of each trading day, so a basket built from today's winners shows an inflated hold (survivorship, ISS-22).",
        "Daily bars are as stored; a corporate action not adjusted in them, or a genuine one-session move above the discontinuity threshold, leaves the instrument out (named), and one below it distorts that instrument.",
        "A hold is not the trading result: one is a position per signal, the other a single purchase; the difference is not a measure of skill.",
        "A basket appears in full wherever its instruments are shared with another, so the baskets cannot be added together.",
    ];

    public async ValueTask<Result<ReportBenchmarksView>> HandleAsync(GetReportBenchmarksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        var ids = ParseIds(query.Basket);
        if (ids.IsFailure)
        {
            return Result.Failure<ReportBenchmarksView>(ids.Error);
        }

        var from = ParseDate(query.From, "from");
        var to = ParseDate(query.To, "to");
        var asOfGiven = ParseDate(query.MembersAsOf, "membersAsOf");
        foreach (var d in new[] { from, to, asOfGiven })
        {
            if (d.IsFailure)
            {
                return Result.Failure<ReportBenchmarksView>(d.Error);
            }
        }

        var runs = RunSelection.Default(await source.ListRunsAsync(cancellationToken)).Chosen.Where(r => r.ClosedTrades > 0).ToList();
        var window = ResolveWindow(from.Value, to.Value, runs);
        if (window.IsFailure)
        {
            return Result.Failure<ReportBenchmarksView>(window.Error);
        }

        var range = window.Value;
        var asOf = asOfGiven.Value ?? MembersDate(ids.Value, runs);
        var members = await LoadMembersAsync(ids.Value, asOf, cancellationToken);
        if (members.IsFailure)
        {
            return Result.Failure<ReportBenchmarksView>(members.Error);
        }

        // Each instrument is read once, however many baskets share it.
        var cache = new Dictionary<Guid, IReadOnlyList<DailyClose>>();
        var options = new BuyAndHoldOptions(settings.MinMembers, settings.MaxStartGapDays, settings.DiscontinuityThreshold);
        var views = new List<BasketBenchmarkView>();
        var included = new HashSet<Guid>();
        foreach (var basket in members.Value)
        {
            var series = new List<InstrumentSeries>();
            foreach (var item in basket.Items)
            {
                series.Add(new InstrumentSeries(item.Instrument.Id, item.Instrument.Symbol, await ReadAsync(item.Instrument.Id, cache, range.From, range.To, cancellationToken)));
            }

            var result = BuyAndHold.Compute(series, range.From, range.To, options);
            var excludedIds = result.Excluded.Select(e => e.Id).ToHashSet();
            included.UnionWith(basket.Items.Select(i => i.Instrument.Id).Where(id => !excludedIds.Contains(id)));
            views.Add(new BasketBenchmarkView(
                basket.BasketId,
                basket.Name,
                basket.Items.Count,
                result.Included,
                ReportViews.Metric(result.Return),
                [.. result.Excluded.Select(e => new BenchmarkExclusionView(e.Id, e.Symbol, e.Reason))]));
        }

        var unavailable = new List<string>();
        var index = await IndexAsync(cache, range, options, unavailable, cancellationToken);
        var excludedCount = views.Sum(v => v.Excluded.Count);
        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.Benchmark(logger, members.Value.Count, cache.Count, excludedCount, elapsed);
        return Result.Success(new ReportBenchmarksView(
            range,
            asOf,
            views,
            index,
            true,
            settings.MinMembers,
            new BenchmarkEvidenceView(Basis, included.Count, range, [.. Limits], unavailable)));
    }

    /// <summary>The India date of the newest run (default run set, with trades) of the asked baskets, else today.</summary>
    private DateOnly MembersDate(List<Guid> ids, List<ReportRunInfo> runs)
    {
        var wanted = ids.Select(id => "basket:" + id.ToString("D")).ToHashSet(StringComparer.Ordinal);
        var newest = runs.Where(r => wanted.Contains(r.Universe)).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        return IndiaTime.Today(newest?.CreatedAt ?? time.GetUtcNow());
    }

    private async ValueTask<Result<List<BasketMembers>>> LoadMembersAsync(List<Guid> ids, DateOnly asOf, CancellationToken cancellationToken)
    {
        var cutoff = BasketMember.CutoffAfter(asOf);
        var loaded = new List<BasketMembers>();
        foreach (var id in ids)
        {
            var members = await baskets.MembersAsync(id, cutoff, asOf, cancellationToken);
            if (members is null || members.ArchivedAt is not null)
            {
                return Result.Failure<List<BasketMembers>>(Error.NotFound("reports.not_found", $"Basket '{id:D}' does not exist or is archived."));
            }

            if (members.Items.Count > settings.MaxInstruments)
            {
                return Result.Failure<List<BasketMembers>>(ReportErrors.Invalid($"Basket '{members.Name}' holds {members.Items.Count} instruments; at most {settings.MaxInstruments} are read."));
            }

            loaded.Add(members);
        }

        var distinct = loaded.SelectMany(m => m.Items).Select(i => i.Instrument.Id).Distinct().Count();
        return distinct > settings.MaxDistinctInstruments
            ? Result.Failure<List<BasketMembers>>(ReportErrors.Invalid($"The baskets hold {distinct} distinct instruments; at most {settings.MaxDistinctInstruments} are read in one call."))
            : Result.Success(loaded);
    }

    private async ValueTask<IndexBenchmarkView> IndexAsync(Dictionary<Guid, IReadOnlyList<DailyClose>> cache, BenchmarkWindowView range, BuyAndHoldOptions options, List<string> unavailable, CancellationToken cancellationToken)
    {
        var configured = await benchmark.GetAsync(cancellationToken);
        if (configured is null)
        {
            const string none = "no benchmark index is in the master list; the owner can track one in Portfolio (this read tracks nothing)";
            unavailable.Add("benchmark index: " + none);
            return new IndexBenchmarkView(null, null, true, new MetricView("unavailable", null, none));
        }

        var inst = configured.Instrument;
        var closes = await ReadAsync(inst.Id, cache, range.From, range.To, cancellationToken);
        var one = BuyAndHold.Compute([new InstrumentSeries(inst.Id, inst.Symbol, closes)], range.From, range.To, options with { MinMembers = 1 });
        var metric = one.Excluded.Count == 0
            ? ReportViews.Metric(one.Return)
            : new MetricView("unavailable", null, closes.Count == 0
                ? $"{inst.Symbol} has no daily bars; the owner can track it in Portfolio (this read tracks nothing)"
                : $"{inst.Symbol} cannot be used over this window ({one.Excluded[0].Reason})");
        if (metric.State == "unavailable")
        {
            unavailable.Add($"benchmark index {inst.Symbol}: {metric.Reason}");
        }

        return new IndexBenchmarkView(inst.Id, inst.Symbol, configured.IsDefault, metric);
    }

    private async ValueTask<IReadOnlyList<DailyClose>> ReadAsync(Guid id, Dictionary<Guid, IReadOnlyList<DailyClose>> cache, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var rows = await bars.ReadDailyBarsAsync(id, from, to.AddDays(1), cancellationToken);
        var closes = rows.Select(r => new DailyClose(IndiaTime.Today(r.Ts), r.Close)).ToList();
        cache[id] = closes;
        return closes;
    }

    private Result<List<Guid>> ParseIds(string? text)
    {
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return Result.Failure<List<Guid>>(ReportErrors.Invalid("Name at least one basket (basket=<id>,<id>)."));
        }

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

        return ids.Count > settings.MaxBaskets
            ? Result.Failure<List<Guid>>(ReportErrors.Invalid($"At most {settings.MaxBaskets} baskets are read at once."))
            : Result.Success(ids);
    }

    private static Result<DateOnly?> ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success<DateOnly?>(null);
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year is >= 2000 and <= 2999
            ? Result.Success<DateOnly?>(date)
            : Result.Failure<DateOnly?>(ReportErrors.Invalid($"{name} must be a date such as 2026-10-11."));
    }

    private Result<BenchmarkWindowView> ResolveWindow(DateOnly? from, DateOnly? to, List<ReportRunInfo> runs)
    {
        var start = from ?? (runs.Count == 0 ? null : runs.Min(r => r.WindowStart) as DateOnly?);
        var end = to ?? (runs.Count == 0 ? null : runs.Max(r => r.WindowEnd) as DateOnly?);
        if (start is null || end is null)
        {
            return Result.Failure<BenchmarkWindowView>(ReportErrors.Invalid("Give from and to: no run in the default run set holds trades to take the window from."));
        }

        if (start > end)
        {
            return Result.Failure<BenchmarkWindowView>(ReportErrors.Invalid("from must not be after to."));
        }

        if (end.Value.DayNumber - start.Value.DayNumber > settings.MaxWindowDays)
        {
            return Result.Failure<BenchmarkWindowView>(ReportErrors.Invalid($"The window is longer than {settings.MaxWindowDays} days."));
        }

        var fromSource = from is null ? "runs" : "query";
        var toSource = to is null ? "runs" : "query";
        return Result.Success(new BenchmarkWindowView(start.Value, end.Value, fromSource == "query" && toSource == "query" ? "query" : "runs", fromSource, toSource));
    }
}
