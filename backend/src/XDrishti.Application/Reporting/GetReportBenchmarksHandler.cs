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

        var (first, last, windowSource) = window.Value;
        var wanted = ids.Value.Select(id => "basket:" + id.ToString("D")).ToHashSet(StringComparer.Ordinal);
        var newest = runs.Where(r => wanted.Contains(r.Universe)).OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        var asOf = asOfGiven.Value ?? (newest is null ? IndiaTime.Today(time.GetUtcNow()) : IndiaTime.Today(newest.CreatedAt));
        var cutoff = BasketMember.CutoffAfter(asOf);

        var loaded = new List<BasketMembers>();
        foreach (var id in ids.Value)
        {
            var members = await baskets.MembersAsync(id, cutoff, asOf, cancellationToken);
            if (members is null || members.ArchivedAt is not null)
            {
                return Result.Failure<ReportBenchmarksView>(Error.NotFound("reports.not_found", $"Basket '{id:D}' does not exist or is archived."));
            }

            if (members.Items.Count > settings.MaxInstruments)
            {
                return Result.Failure<ReportBenchmarksView>(ReportErrors.Invalid($"Basket '{members.Name}' holds {members.Items.Count} instruments; at most {settings.MaxInstruments} are read."));
            }

            loaded.Add(members);
        }

        // Each instrument is read once, however many baskets share it.
        var cache = new Dictionary<Guid, IReadOnlyList<DailyClose>>();
        var options = new BuyAndHoldOptions(settings.MinMembers, settings.MaxStartGapDays, settings.DiscontinuityThreshold);
        var views = new List<BasketBenchmarkView>();
        var included = new HashSet<Guid>();
        foreach (var basket in loaded)
        {
            var series = new List<InstrumentSeries>();
            foreach (var item in basket.Items)
            {
                series.Add(new InstrumentSeries(item.Instrument.Symbol, await ReadAsync(item.Instrument.Id, cache, first, last, cancellationToken)));
            }

            var result = BuyAndHold.Compute(series, first, last, options);
            var excludedSymbols = result.Excluded.Select(e => e.Symbol).ToHashSet(StringComparer.Ordinal);
            foreach (var item in basket.Items.Where(i => !excludedSymbols.Contains(i.Instrument.Symbol)))
            {
                included.Add(item.Instrument.Id);
            }

            views.Add(new BasketBenchmarkView(
                basket.BasketId,
                basket.Name,
                basket.Items.Count,
                result.Included,
                ReportViews.Metric(result.Return),
                [.. result.Excluded.Select(e => new BenchmarkExclusionView(e.Symbol, e.Reason))]));
        }

        var configured = await benchmark.GetAsync(cancellationToken);
        var unavailable = new List<string>();
        IndexBenchmarkView index;
        if (configured is null)
        {
            const string none = "no benchmark index is in the master list; the owner can track one in Portfolio (this read tracks nothing)";
            unavailable.Add("benchmark index: " + none);
            index = new IndexBenchmarkView(null, null, true, new MetricView("unavailable", null, none));
        }
        else
        {
            var inst = configured.Instrument;
            var closes = await ReadAsync(inst.Id, cache, first, last, cancellationToken);
            var one = BuyAndHold.Compute([new InstrumentSeries(inst.Symbol, closes)], first, last, options with { MinMembers = 1 });
            var metric = one.Excluded.Count == 0
                ? ReportViews.Metric(one.Return)
                : new MetricView("unavailable", null, closes.Count == 0
                    ? $"{inst.Symbol} has no daily bars; the owner can track it in Portfolio (this read tracks nothing)"
                    : $"{inst.Symbol} cannot be used over this window ({one.Excluded[0].Reason})");
            if (metric.State == "unavailable")
            {
                unavailable.Add($"benchmark index {inst.Symbol}: {metric.Reason}");
            }

            index = new IndexBenchmarkView(inst.Id, inst.Symbol, configured.IsDefault, metric);
        }

        var range = new BenchmarkWindowView(first, last, windowSource);
        var excludedCount = views.Sum(v => v.Excluded.Count);
        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.Benchmark(logger, loaded.Count, cache.Count, excludedCount, elapsed);
        return Result.Success(new ReportBenchmarksView(
            range,
            asOf,
            views,
            index,
            true,
            settings.MinMembers,
            new BenchmarkEvidenceView(Basis, included.Count, range, [.. Limits], unavailable)));
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

    private static Result<(DateOnly From, DateOnly To, string Source)> ResolveWindow(DateOnly? from, DateOnly? to, List<ReportRunInfo> runs)
    {
        var source = from is not null && to is not null ? "query" : "runs";
        var start = from ?? (runs.Count == 0 ? null : runs.Min(r => r.WindowStart) as DateOnly?);
        var end = to ?? (runs.Count == 0 ? null : runs.Max(r => r.WindowEnd) as DateOnly?);
        if (start is null || end is null)
        {
            return Result.Failure<(DateOnly, DateOnly, string)>(ReportErrors.Invalid("Give from and to: no run in the default run set holds trades to take the window from."));
        }

        return start > end
            ? Result.Failure<(DateOnly, DateOnly, string)>(ReportErrors.Invalid("from must not be after to."))
            : Result.Success((start.Value, end.Value, source));
    }
}
