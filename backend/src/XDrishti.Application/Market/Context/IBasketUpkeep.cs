using XDrishti.Application.Market.Bars.Pipeline;

namespace XDrishti.Application.Market.Context;

/// <summary>The work on the basket context and the relative strength that follows every data job that aggregated; failures are warnings of the job, never errors.</summary>
internal interface IBasketUpkeep
{
    ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken);
}
