using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>The latest bars of an instrument in one timeframe (<c>1m 5m 10m 15m 30m 1h 1D 1W 1M</c>, default <c>1D</c>), newest first; <c>take</c> 1 to 1000, default 100.</summary>
public sealed record GetInstrumentBarsQuery(Guid InstrumentId, string? Timeframe, int? Take) : IQuery<Result<InstrumentBars>>;

internal sealed class GetInstrumentBarsHandler(IBarQueries queries) : IQueryHandler<GetInstrumentBarsQuery, Result<InstrumentBars>>
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
        return Result.Success(new InstrumentBars(instrument.Id, instrument.Symbol, instrument.Name, timeframe.ToCode(), rows));
    }
}
