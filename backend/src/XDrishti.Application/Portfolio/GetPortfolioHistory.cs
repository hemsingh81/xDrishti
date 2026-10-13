using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// The value history of the lots of a scope against the same cash flows in the benchmark index, with unit-value (NAV) series and the maximum drawdown (ADR 0020, P2-07). Computed on read from the lots and the stored day bars, adjusted for
/// corporate actions (see <see cref="PortfolioHistoryCalculator"/>): one query for the holdings and their lots, one for the day bars of every instrument of the scope and the benchmark, one for their applied actions. Today's live point (prices of today
/// from quotes or the last sync, from the session's open) is added when today is a trading day. <c>from</c>/<c>to</c> (<c>yyyy-MM-dd</c>) cut the range (the NAV is not rebased); <c>resolution</c> <c>day|week|month</c> keeps the last trading day of each period; without it the finest
/// resolution with at most 2,000 points is used, and a given one that gives more is <c>portfolio.invalid</c>. Cached for 60 seconds per scope, range and resolution; anything that changes lots, holdings or corporate actions drops the cache.
/// </summary>
public sealed record GetPortfolioHistoryQuery(HistoryRequest Request) : IQuery<Result<PortfolioHistory>>;

internal sealed class GetPortfolioHistoryHandler(
    IHoldingQueries holdings,
    IPortfolioBenchmark benchmarks,
    IPortfolioPrices prices,
    ITradingCalendar calendar,
    IPortfolioHistoryCache cache,
    TimeProvider clock,
    ILogger<GetPortfolioHistoryHandler> logger) : IQueryHandler<GetPortfolioHistoryQuery, Result<PortfolioHistory>>
{
    /// <summary>The most points one answer carries.</summary>
    public const int MaxPoints = 2000;

    /// <summary>Days read before the earliest lot, so a lot dated on a holiday or a weekend finds the close before it.</summary>
    public const int LookbackDays = 14;

    /// <summary>
    /// The most stored day bars one request may read (instruments × trading days from 14 days before the earliest lot to today, estimated before anything is read; the calculators hold about this many prices at once). 2,000,000 allows 300 holdings
    /// over about 26 years; a request beyond it is <c>portfolio.invalid</c>. If it ever grows, the adjusted closes can be cached per instrument.
    /// </summary>
    public const int MaxBarRows = 2_000_000;

    private static readonly DateOnly EarliestDate = new(1990, 1, 1);

    public async ValueTask<Result<PortfolioHistory>> HandleAsync(GetPortfolioHistoryQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var parsed = Parse(request);
        if (parsed.IsFailure)
        {
            return Result.Failure<PortfolioHistory>(parsed.Error);
        }

        var (from, to, resolution) = parsed.Value;
        var key = string.Join('|', request.AccountId?.ToString("N") ?? "-", request.BucketId?.ToString("N") ?? "-", from?.ToString("O") ?? "-", to?.ToString("O") ?? "-", resolution is { } r ? HistoryResolutions.Name(r) : "auto");
        if (cache.TryGet(key, out PortfolioHistory? cached))
        {
            PortfolioHistoryLog.Cached(logger, cached!.Points.Count);
            return Result.Success(cached);
        }

        var version = cache.Version;
        var started = Stopwatch.GetTimestamp();
        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var data = await holdings.ListAsync(new HoldingFilter(request.AccountId, request.BucketId, null, null, TotalsOnly: request.AccountId is null), cancellationToken);
        var configured = await benchmarks.GetAsync(cancellationToken);
        var withLots = data.Where(h => h.Lots.Count > 0).ToList();
        if (withLots.Count == 0)
        {
            return Result.Success(Remember(key, version, Empty(configured, "There are no holdings in this scope.", resolution ?? HistoryResolution.Day)));
        }

        var earliest = withLots.SelectMany(h => h.Lots).Min(l => l.AcquiredOn);
        var instrumentIds = withLots.Where(h => h.InstrumentId is not null).Select(h => h.InstrumentId!.Value).Distinct().ToList();
        var wanted = configured is { } c ? [.. instrumentIds.Append(c.Instrument.Id).Distinct()] : instrumentIds;
        var estimatedRows = (long)wanted.Count * EstimatedTradingDays(earliest.AddDays(-LookbackDays), today);
        if (estimatedRows > MaxBarRows)
        {
            return Result.Failure<PortfolioHistory>(PortfolioErrors.Invalid(
                "scope",
                $"The history would read about {estimatedRows:N0} daily prices (at most {MaxBarRows:N0}): a lot is dated back to {earliest:yyyy-MM-dd}. Correct the oldest lot dates or choose one account or bucket."));
        }

        var stored = wanted.Count == 0
            ? new Dictionary<Guid, StoredPrices>()
            : new Dictionary<Guid, StoredPrices>(await prices.ReadDailyAsync(wanted, earliest.AddDays(-LookbackDays), today, cancellationToken));
        var tradingCalendar = await calendar.GetAsync(cancellationToken);

        var closes = new Dictionary<Guid, IReadOnlyList<DailyClose>>();
        foreach (var id in instrumentIds)
        {
            if (stored.TryGetValue(id, out var own))
            {
                closes[id] = AdjustedCloses.Adjust(own.Closes, AdjustmentFactors.From(own.Actions));
            }
        }

        IReadOnlyList<DailyClose> benchmarkCloses = configured is { } b && stored.TryGetValue(b.Instrument.Id, out var index)
            ? AdjustedCloses.Adjust(index.Closes, AdjustmentFactors.From(index.Actions))
            : [];
        stored.Clear(); // the raw closes are not needed any more

        var series = PortfolioHistoryCalculator.Build(new HistoryInput(data, closes, benchmarkCloses, tradingCalendar, LiveOf(data, tradingCalendar, now, today)));
        var range = series.Points.Where(p => (from is null || p.Date >= from) && (to is null || p.Date <= to)).ToList();

        var chosen = Choose(range, resolution);
        if (chosen.IsFailure)
        {
            return Result.Failure<PortfolioHistory>(chosen.Error);
        }

        var (points, used) = chosen.Value;
        var includesLast = range.Count > 0 && series.Points.Count > 0 && range[^1].Date == series.Points[^1].Date;
        var history = new PortfolioHistory(
            points,
            series.Coverage,
            series.Partial,
            BenchmarkView(configured, series.Benchmark, benchmarkCloses, series.BenchmarkAsOf),
            DrawdownCalculator.OfPortfolio(range),
            DrawdownCalculator.OfBenchmark(range),
            series.Points.Count == 0 ? null : series.Points[0].Date,
            includesLast ? series.LastPriceAt : null,
            HistoryResolutions.Name(used),
            range.Count == 0 ? null : range[0].Date,
            range.Count == 0 ? null : range[^1].Date);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        PortfolioHistoryLog.Computed(logger, data.Count, instrumentIds.Count, series.Points.Count, points.Count, elapsedMs);
        return Result.Success(Remember(key, version, history));
    }

    private PortfolioHistory Remember(string key, long version, PortfolioHistory history)
    {
        cache.Set(key, history, version);
        return history;
    }

    private static Result<(DateOnly? From, DateOnly? To, HistoryResolution? Resolution)> Parse(HistoryRequest request)
    {
        var errors = new FieldErrors();
        var from = Date(request.From, "from", errors);
        var to = Date(request.To, "to", errors);
        HistoryResolution? resolution = null;
        if (!string.IsNullOrWhiteSpace(request.Resolution))
        {
            if (HistoryResolutions.TryParse(request.Resolution, out var parsed))
            {
                resolution = parsed;
            }
            else
            {
                errors.Add("resolution", "Use day, week or month.");
            }
        }

        if (from is not null && to is not null && to < from)
        {
            errors.Add("to", "The end is before the start.");
        }

        return errors.HasErrors
            ? Result.Failure<(DateOnly?, DateOnly?, HistoryResolution?)>(PortfolioErrors.Invalid(errors))
            : Result.Success((from, to, resolution));
    }

    private static DateOnly? Date(string? text, string field, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date < EarliestDate)
        {
            errors.Add(field, "Use the date as yyyy-MM-dd (1990-01-01 or later).");
            return null;
        }

        return date;
    }

    /// <summary>The points at the given resolution, or at the finest one with at most <see cref="MaxPoints"/> when none is given.</summary>
    private static Result<(IReadOnlyList<HistoryPoint> Points, HistoryResolution Used)> Choose(List<HistoryPoint> range, HistoryResolution? resolution)
    {
        if (resolution is { } given)
        {
            var thinned = HistoryResolutions.Thin(range, given);
            return thinned.Count <= MaxPoints
                ? Result.Success((thinned, given))
                : Result.Failure<(IReadOnlyList<HistoryPoint>, HistoryResolution)>(PortfolioErrors.Invalid(
                    "resolution", $"That range has {thinned.Count:N0} points at that resolution; at most {MaxPoints:N0} are returned. Use a coarser resolution or a shorter range."));
        }

        // Every date is 1990 or later, so months never exceed the limit: the last candidate always fits.
        var resolutionFound = HistoryResolution.Day;
        var found = HistoryResolutions.Thin(range, resolutionFound);
        foreach (var coarser in new[] { HistoryResolution.Week, HistoryResolution.Month })
        {
            if (found.Count <= MaxPoints)
            {
                break;
            }

            resolutionFound = coarser;
            found = HistoryResolutions.Thin(range, coarser);
        }

        return Result.Success((found, resolutionFound));
    }

    /// <summary>
    /// Today's prices of the open holdings: only from the session's open on a trading day, and only prices of today (a quote or a sync; an older price would be yesterday's close again). The newest price wins when two
    /// accounts hold the same instrument.
    /// </summary>
    private static LivePrices? LiveOf(IReadOnlyList<HoldingData> data, TradingCalendar tradingCalendar, DateTimeOffset now, DateOnly today)
    {
        var ist = IndiaTime.ToIst(now);
        if (tradingCalendar.SessionOf(today) is not { } session || (ist.Hour * 60) + ist.Minute < session.FromMinute)
        {
            return null;
        }

        var live = new Dictionary<Guid, (decimal Price, DateTimeOffset At)>();
        foreach (var holding in data.Where(h => h.IsOpen && h.InstrumentId is not null && h.LastPrice > 0m && h.LastPriceAt is { } at && IndiaTime.Today(at) == today))
        {
            var id = holding.InstrumentId!.Value;
            if (!live.TryGetValue(id, out var existing) || holding.LastPriceAt > existing.At)
            {
                live[id] = (holding.LastPrice, holding.LastPriceAt!.Value);
            }
        }

        return live.Count == 0 ? null : new LivePrices(today, live.ToDictionary(x => x.Key, x => x.Value.Price), live.Values.Max(x => x.At));
    }

    /// <summary>Weekdays from <paramref name="from"/> to <paramref name="to"/>, rounded up (an upper bound of the trading days; holidays are not looked up for a size guess).</summary>
    private static long EstimatedTradingDays(DateOnly from, DateOnly to) => Math.Max(0, to.DayNumber - from.DayNumber + 1) * 5L / 7L + 1;

    private static HistoryBenchmark BenchmarkView(ConfiguredBenchmark? configured, BenchmarkAvailability availability, IReadOnlyList<DailyClose> closes, DateOnly? asOf)
    {
        if (configured is not { } c)
        {
            return new HistoryBenchmark("unavailable", null, null, null, false, false, "The benchmark index is not in the instrument master yet. Refresh the instrument list.", null);
        }

        var instrument = c.Instrument;
        var reason = availability switch
        {
            BenchmarkAvailability.Available => null,
            BenchmarkAvailability.NoBars => instrument.Tracked
                ? $"{instrument.Name} has no price history yet. Run Update data to fetch it."
                : $"{instrument.Name} has no price history. Track it, then run Update data.",
            BenchmarkAvailability.StartsAfterFirstFlow => $"{instrument.Name} history starts on {closes[0].Date:d MMM yyyy}, after your first investment.",
            BenchmarkAvailability.EndsBeforeFirstFlow => $"{instrument.Name} history ends on {closes[^1].Date:d MMM yyyy}, before your first investment. Run Update data.",
            _ => "There is nothing to compare yet.",
        };
        return new HistoryBenchmark(availability == BenchmarkAvailability.Available ? "ok" : "unavailable", instrument.Id, instrument.Symbol, instrument.Name, instrument.Tracked, instrument.HasBars, reason, availability == BenchmarkAvailability.Available ? asOf : null);
    }

    private static PortfolioHistory Empty(ConfiguredBenchmark? configured, string reason, HistoryResolution resolution)
    {
        var view = BenchmarkView(configured, BenchmarkAvailability.NoFlows, [], null);
        return new PortfolioHistory([], new HistoryCoverage(0, 0, 0, 0), false, view with { Reason = reason }, null, null, null, null, HistoryResolutions.Name(resolution), null, null);
    }
}
