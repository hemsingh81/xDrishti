using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// Where a report over a simulate-everything job reads its training runs (spec AU-14, ADR 0243 point 7, ADR 0250). It is NOT evidence and it is not <see cref="IReportTradeSource"/>: the evidence reports never see a training run. Its one implementation reads only through
/// the one reader of training runs, which carries the training fence, so nothing here can reach a research run.
/// </summary>
public interface ITrainingReportSource
{
    /// <summary>The closed trades of the chunks of the job that were done by <paramref name="finishedBy"/> and pass the filter, streamed, in the order of run then ordinal.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="finishedBy">Only chunks finished at or before this instant are read.</param>
    /// <param name="filter">The filter pushed down to the store.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    IAsyncEnumerable<ReportTrade> StreamTradesAsync(Guid jobId, DateTimeOffset finishedBy, ReportTradeFilter filter, CancellationToken cancellationToken);

    /// <summary>The headers of the training runs with these keys.</summary>
    /// <param name="runKeys">The run keys (at most 100,000).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    ValueTask<IReadOnlyList<TrainingRunHeader>> ListRunsAsync(IReadOnlyList<string> runKeys, CancellationToken cancellationToken);
}
