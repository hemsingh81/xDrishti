using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// The latest bars of an instrument in one timeframe (<c>1m 5m 10m 15m 30m 1h 1D 1W 1M</c>, default <c>1D</c>), newest first; <c>take</c> 1 to 1000, default 100. Raw prices by default; with <c>Adjusted</c> the
/// prices are adjusted for the applied corporate actions on read: each bar by the factor of its India date, see <see cref="AdjustmentFactors"/>. A minute or intraday bar and a day bar lie within one
/// day, so that factor is exact for them. A week or month bar that contains an ex-date is built again from the adjusted day bars of its bucket (the open of its first day, the highest high, the lowest low,
/// the close of its last day, the volumes added); one that lies wholly before or after the ex-dates has one factor for all its days and is adjusted like the others.
/// </summary>
public sealed record GetInstrumentBarsQuery(Guid InstrumentId, string? Timeframe, int? Take, bool Adjusted = false) : IQuery<Result<InstrumentBars>>;

internal sealed class GetInstrumentBarsHandler(IBarQueries queries, IAdjustmentFactors adjustments) : IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>>
{
    public async ValueTask<Result<InstrumentBars>> HandleAsync(GetInstrumentBarsQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var timeframe = Timeframe.D1;
        if (!string.IsNullOrWhiteSpace(query.Timeframe) && !Timeframes.TryParse(query.Timeframe.Trim(), out timeframe))
        {
            errors.Add("timeframe", "Use 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W or 1M.");
        }

        var take = query.Take ?? DataLimits.DefaultBars;
        if (take is < 1 or > DataLimits.MaxBars)
        {
            errors.Add("take", $"Use 1 to {DataLimits.MaxBars}.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<InstrumentBars>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."));
        }

        var instrument = await queries.FindInstrumentAsync(query.InstrumentId, cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<InstrumentBars>(DataErrors.InstrumentNotFound);
        }

        var rows = await queries.ReadBarsAsync(instrument.Id, timeframe, take, cancellationToken);
        if (!query.Adjusted)
        {
            return Result.Success(new InstrumentBars(instrument.Id, instrument.Symbol, instrument.Name, timeframe.ToCode(), rows));
        }

        var factors = await adjustments.GetAsync(instrument.Id, cancellationToken);
        var items = timeframe is Timeframe.W1 or Timeframe.Mo1
            ? await AdjustBucketsAsync(instrument.Id, timeframe, rows, factors, cancellationToken)
            : [.. rows.Select(row => Adjust(row, factors))];
        return Result.Success(new InstrumentBars(instrument.Id, instrument.Symbol, instrument.Name, timeframe.ToCode(), items, Describe(rows, factors)));
    }

    private async ValueTask<IReadOnlyList<BarRow>> AdjustBucketsAsync(Guid instrumentId, Timeframe timeframe, IReadOnlyList<BarRow> rows, AdjustmentFactors factors, CancellationToken cancellationToken)
    {
        var straddling = rows.Where(row => Contains(factors, Bucket(timeframe, row))).ToList();
        IReadOnlyList<BarRow> days = [];
        if (straddling.Count > 0)
        {
            var first = Bucket(timeframe, straddling.MinBy(row => row.Ts)!);
            var last = Bucket(timeframe, straddling.MaxBy(row => row.Ts)!);
            days = await queries.ReadDailyBarsAsync(instrumentId, first.Start, last.EndExclusive, cancellationToken);
        }

        var result = new List<BarRow>(rows.Count);
        foreach (var row in rows)
        {
            var bucket = Bucket(timeframe, row);
            var inBucket = Contains(factors, bucket) ? days.Where(d => IndiaTime.Today(d.Ts) >= bucket.Start && IndiaTime.Today(d.Ts) < bucket.EndExclusive).ToList() : [];
            result.Add(inBucket.Count == 0 ? Adjust(row, factors) : Rebuild(row, inBucket, factors));
        }

        return result;
    }

    private static (DateOnly Start, DateOnly EndExclusive) Bucket(Timeframe timeframe, BarRow row)
    {
        var start = IndiaTime.Today(row.Ts);
        return (start, timeframe == Timeframe.W1 ? start.AddDays(7) : start.AddMonths(1));
    }

    // An ex-date inside the bucket, after its first day: the days before it and the days from it on have different factors.
    private static bool Contains(AdjustmentFactors factors, (DateOnly Start, DateOnly EndExclusive) bucket) =>
        factors.ActionsAfter(bucket.Start).Any(a => a.ExDate < bucket.EndExclusive);

    private static BarRow Rebuild(BarRow bucket, List<BarRow> days, AdjustmentFactors factors)
    {
        var adjusted = days.Select(day => Adjust(day, factors)).ToList();
        return bucket with
        {
            Open = adjusted[0].Open,
            High = adjusted.Max(d => d.High),
            Low = adjusted.Min(d => d.Low),
            Close = adjusted[^1].Close,
            Volume = adjusted.Sum(d => d.Volume),
        };
    }

    private static BarRow Adjust(BarRow row, AdjustmentFactors factors)
    {
        var factor = factors.FactorAt(IndiaTime.Today(row.Ts));
        return factor == 1.0
            ? row
            : row with
            {
                Open = AdjustmentFactors.Price(row.Open, factor),
                High = AdjustmentFactors.Price(row.High, factor),
                Low = AdjustmentFactors.Price(row.Low, factor),
                Close = AdjustmentFactors.Price(row.Close, factor),
                Volume = AdjustmentFactors.Volume(row.Volume, factor),
            };
    }

    // The actions that changed the bars read are those with an ex-date after the oldest bar (rows are newest first); the factor is the oldest bar's.
    private static AdjustmentView Describe(IReadOnlyList<BarRow> rows, AdjustmentFactors factors)
    {
        if (rows.Count == 0)
        {
            return new AdjustmentView(0, 1.0, []);
        }

        var oldest = IndiaTime.Today(rows[^1].Ts);
        var actions = factors.ActionsAfter(oldest);
        return new AdjustmentView(actions.Count, Math.Round(factors.FactorAt(oldest), CorporateActionRatio.FactorDecimals), [.. actions.Select(a => new AdjustmentActionView(a.ExDate, a.Kind.ToCode(), a.Ratio, (double)a.Factor))]);
    }
}
