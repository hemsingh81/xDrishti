using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// One instrument's quality: every check of its current result, the missing minutes (at most 500 listed, with the total), and the stored buckets of the window next to the ones recomputed from the 1-minute
/// bars now (<c>differencesOnly</c> keeps the differing ones; <c>timeframe</c> one timeframe: 5m 10m 15m 30m 1h 1D 1W 1M). <c>data.instrument_not_found</c>, <c>data.invalid</c> for a timeframe that does not exist.
/// </summary>
public sealed record GetQualityInstrumentQuery(Guid InstrumentId, bool? DifferencesOnly, string? Timeframe) : IQuery<Result<QualityInstrumentDetail>>;

internal sealed class GetQualityInstrumentHandler(
    IQualityQueries queries,
    IQualityData data,
    IBarStore bars,
    ITradingCalendar tradingCalendar,
    IDataSettings settings,
    TimeProvider clock) : IQueryHandler<GetQualityInstrumentQuery, Result<QualityInstrumentDetail>>
{
    /// <summary>The most missing minutes listed.</summary>
    public const int MaxMissingMinutes = 500;

    /// <summary>The most bucket rows listed.</summary>
    public const int MaxBucketRows = 2000;

    public async ValueTask<Result<QualityInstrumentDetail>> HandleAsync(GetQualityInstrumentQuery query, CancellationToken cancellationToken)
    {
        Timeframe? only = null;
        if (!string.IsNullOrWhiteSpace(query.Timeframe))
        {
            if (!Timeframes.TryParse(query.Timeframe.Trim(), out var parsed) || !Timeframes.Aggregated.Contains(parsed))
            {
                return Result.Failure<QualityInstrumentDetail>(DataErrors.Invalid("timeframe", "Use 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M."));
            }

            only = parsed;
        }

        var found = await queries.FindAsync(query.InstrumentId, cancellationToken);
        if (found is null)
        {
            return Result.Failure<QualityInstrumentDetail>(DataErrors.InstrumentNotFound);
        }

        var buckets = await BucketsAsync(found, only, query.DifferencesOnly ?? false, cancellationToken);
        return Result.Success(new QualityInstrumentDetail(
            found.InstrumentId,
            found.Symbol,
            found.Name,
            found.Type,
            found.Tracked,
            found.Source,
            found.FirstBar is { } first ? IndiaTime.ToIst(first) : null,
            found.LastBar is { } last ? IndiaTime.ToIst(last) : null,
            found.Status ?? QualityInstrumentSearch.NotChecked,
            found.Score,
            found.BarsChecked,
            found.RunId,
            found.CheckedAt is { } at ? IndiaTime.ToIst(at) : null,
            found.Checks is { } checks ? new QualityCheckResults(checks.Completeness, checks.Sanity, checks.Spikes, QualityRows.InOrder(checks.Aggregates), checks.Freshness, checks.OfficialDaily) : null,
            Missing(found.Checks),
            buckets));
    }

    internal static QualityMissing Missing(QualityChecks? checks)
    {
        if (checks is null)
        {
            return new QualityMissing(0, false, []);
        }

        var items = new List<MissingMinute>();
        foreach (var range in checks.Details?.Missing ?? [])
        {
            for (var minute = range.FromMinute; minute < range.FromMinute + range.Minutes && items.Count < MaxMissingMinutes; minute++)
            {
                items.Add(new MissingMinute(range.Date, $"{minute / 60:00}:{minute % 60:00}"));
            }
        }

        var total = checks.Completeness.MissingMinutes;
        return new QualityMissing(total, items.Count < total, items);
    }

    private async ValueTask<QualityBuckets> BucketsAsync(StoredQualityResult found, Timeframe? only, bool differencesOnly, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = QualityWindow.Sessions(await tradingCalendar.GetAsync(cancellationToken), now, settings.QualitySessions);
        if (sessions.Count == 0 || found.FirstBar is null)
        {
            return new QualityBuckets(now, 0, 0, false, []);
        }

        var bounds = QualityWindow.Bounds(sessions[0]);
        var minuteBars = await bars.ReadAsync(found.InstrumentId, DateTimeOffset.FromUnixTimeSeconds(bounds.ReadFrom), now.AddDays(2), cancellationToken);
        var stored = await data.ReadStoredBucketsAsync(found.InstrumentId, bounds, cancellationToken);
        var rows = AggregateChecker.Compare(minuteBars, stored, bounds).Where(r => only is null || r.Timeframe == only).ToList();
        var shown = rows.Where(r => !differencesOnly || r.Differs).ToList();
        return new QualityBuckets(now, rows.Count, rows.Count(r => r.Differs), shown.Count > MaxBucketRows, [.. shown.Take(MaxBucketRows).Select(Row)]);
    }

    private static QualityBucketRow Row(BucketComparison r) =>
        new(r.Timeframe.ToCode(), r.Ts, r.Stored is { } s ? Values(s.Values) : null, r.Recomputed is { } c ? Values(c) : null, r.Differs, r.Fields);

    private static QualityBucketValues Values(AggBucket b) => new(b.Open, b.High, b.Low, b.Close, b.Volume, b.Bars);
}
