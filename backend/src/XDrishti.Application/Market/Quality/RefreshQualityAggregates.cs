using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// Fix action: rebuilds the derived timeframes of one instrument over the quality window from its stored 1-minute bars, then checks the instrument again. A data job with the steps <c>aggregate</c> and
/// <c>quality</c> for that instrument. <c>data.instrument_not_found</c>, <c>data.job_running</c>.
/// </summary>
public sealed record RefreshQualityAggregatesCommand(Guid InstrumentId, string RequestedBy) : ICommand;

internal sealed class RefreshQualityAggregatesHandler(
    IBarQueries queries,
    ICommandHandler<StartDataJobCommand, DataJobView> start) : ICommandHandler<RefreshQualityAggregatesCommand, DataJobView>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(RefreshQualityAggregatesCommand command, CancellationToken cancellationToken)
    {
        if (await queries.FindInstrumentAsync(command.InstrumentId, cancellationToken) is null)
        {
            return Result.Failure<DataJobView>(DataErrors.InstrumentNotFound);
        }

        return await start.HandleAsync(
            new StartDataJobCommand(new StartDataJobRequest([DataSteps.Aggregate, DataSteps.Quality]), command.RequestedBy, DataJobTrigger.Manual, command.InstrumentId),
            cancellationToken);
    }
}
