using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Reporting;

namespace XDrishti.Infrastructure.SimAll;

/// <summary>The report side of the training runs (spec AU-14): a thin hand-over to the one reader of training runs, so the report engine never holds a reader that could see a research run or a connection of its own.</summary>
internal sealed class TrainingReportSource(ITrainingRunReader reader) : ITrainingReportSource
{
    public IAsyncEnumerable<ReportTrade> StreamTradesAsync(Guid jobId, DateTimeOffset finishedBy, ReportTradeFilter filter, CancellationToken cancellationToken) =>
        reader.StreamJobTradesAsync(jobId, finishedBy, filter, cancellationToken);

    public ValueTask<IReadOnlyList<TrainingRunHeader>> ListRunsAsync(IReadOnlyList<string> runKeys, CancellationToken cancellationToken) =>
        reader.ListByKeysAsync(runKeys, cancellationToken);
}
