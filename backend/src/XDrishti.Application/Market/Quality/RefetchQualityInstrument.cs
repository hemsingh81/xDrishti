using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// Fix action: fetches the instrument's missing bars from Dhan again (its gap days, including the ones given up on as unfillable), rebuilds its aggregates and checks it again. A data job with the steps
/// <c>dhan</c>, <c>aggregate</c> and <c>quality</c> for that instrument and <c>refetchGaps</c>. <c>data.instrument_not_found</c>; 409 <c>data.dhan_not_available</c> / <c>data.dhan_not_connected</c> when Dhan cannot
/// be asked (or the instrument has no Dhan id); <c>data.job_running</c>.
/// </summary>
public sealed record RefetchQualityInstrumentCommand(Guid InstrumentId, string RequestedBy) : ICommand;

internal sealed class RefetchQualityInstrumentHandler(
    IBarQueries queries,
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    ICommandHandler<StartDataJobCommand, DataJobView> start) : ICommandHandler<RefetchQualityInstrumentCommand, DataJobView>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(RefetchQualityInstrumentCommand command, CancellationToken cancellationToken)
    {
        if (await queries.FindInstrumentAsync(command.InstrumentId, cancellationToken) is null)
        {
            return Result.Failure<DataJobView>(DataErrors.InstrumentNotFound);
        }

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return Result.Failure<DataJobView>(Error.Conflict(DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available."));
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return Result.Failure<DataJobView>(Error.Conflict(DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account."));
        }

        if (!(await targets.ListAsync(cancellationToken)).Any(t => t.InstrumentId == command.InstrumentId))
        {
            return Result.Failure<DataJobView>(Error.Conflict(DataErrors.DhanNotAvailableCode, "Dhan cannot be asked for this instrument: it is not tracked, is retired or has no Dhan id."));
        }

        return await start.HandleAsync(
            new StartDataJobCommand(
                new StartDataJobRequest([DataSteps.Dhan, DataSteps.Aggregate, DataSteps.Quality], RefetchGaps: true),
                command.RequestedBy,
                DataJobTrigger.Manual,
                command.InstrumentId),
            cancellationToken);
    }
}
