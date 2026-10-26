using XDrishti.Application.Market.Bars.Pipeline;

namespace XDrishti.Application.Market.Context;

/// <summary>The work on the market context that follows every data job that aggregated; failures are warnings of the job, never errors.</summary>
internal interface IContextUpkeep
{
    ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken);
}
