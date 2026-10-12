using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Review;

/// <summary>
/// Undoes the load of one CSV file (ADR 0017 §6) as a data job: its CSV phase removes the file's <c>csv</c>-source bars of the file's instrument within the file's recorded first-to-last bar (Dhan bars stay), the file is
/// pending again, then the aggregate phase rebuilds the instrument. Values an earlier load had overwritten are not restored. <c>data.csv_file_not_found</c> (404) when the file was never loaded (or is undone already),
/// <c>data.job_running</c> (409) while any data job runs. Audited as "CSV load of &lt;file&gt; undone, N bars removed" when the job removes the bars.
/// </summary>
public sealed record UndoCsvLoadCommand(string FileName, string RequestedBy) : ICommand;

internal sealed class UndoCsvLoadHandler(
    ICsvFileLog log,
    ICommandHandler<StartDataJobCommand, DataJobView> start) : ICommandHandler<UndoCsvLoadCommand, DataJobView>
{
    public async ValueTask<Result<DataJobView>> HandleAsync(UndoCsvLoadCommand command, CancellationToken cancellationToken)
    {
        var name = command.FileName;
        if (!CsvFileName.TryParse(name, out _))
        {
            return Result.Failure<DataJobView>(DataErrors.CsvFileNotFound);
        }

        var loaded = await log.ListAsync(cancellationToken);
        if (!loaded.TryGetValue(name, out var file) || file.InstrumentId is not { } instrumentId)
        {
            return Result.Failure<DataJobView>(DataErrors.CsvFileNotFound);
        }

        return await start.HandleAsync(
            new StartDataJobCommand(new StartDataJobRequest(), command.RequestedBy, DataJobTrigger.Manual, instrumentId, name),
            cancellationToken);
    }
}
