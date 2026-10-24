using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Abstractions.Reporting;

/// <summary>
/// Where a report reads the stored simulation runs and their closed trades (P8-06, ADR 0100). Read only: the store is the result store of P3-10 seen through a projection of the columns a report needs, streamed without a change tracker. Open-at-end trades
/// are never returned (a report counts closed trades only).
/// </summary>
public interface IReportTradeSource
{
    /// <summary>Every stored run, newest first, with the facts and labels a report may use.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    ValueTask<IReadOnlyList<ReportRunInfo>> ListRunsAsync(CancellationToken cancellationToken);

    /// <summary>The closed trades of the named runs that pass the filter, in the order of the stored record, one run after the other. A run key that is not stored gives no trades.</summary>
    /// <param name="runKeys">The run keys.</param>
    /// <param name="filter">The filter pushed down to the store.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    IAsyncEnumerable<ReportTrade> StreamTradesAsync(IReadOnlyList<string> runKeys, ReportTradeFilter filter, CancellationToken cancellationToken);
}
