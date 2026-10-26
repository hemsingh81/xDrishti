using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Pipeline;

namespace XDrishti.Application.Market.Context;

/// <summary>After a data job that aggregated, writes the context days that became final (P3-01d). Never fails the job: whatever goes wrong is a warning of the job, a missing index is only logged (the coverage endpoint names it).</summary>
internal sealed class ContextUpkeep(
    ICommandHandler<RefreshContextCommand, ContextRefreshed> refresh,
    ILogger<ContextUpkeep> logger) : IContextUpkeep
{
    private const string Crashed = "The market context could not be updated. See the API log.";

    public async ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        try
        {
            var result = await refresh.HandleAsync(new RefreshContextCommand(), cancellationToken);
            if (result.IsFailure)
            {
                await reporter.WarnAsync("Market context: " + result.Error.Message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the context is a side effect of the job: whatever goes wrong is a warning, the job's own result stands
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ContextLog.UpkeepCrashed(logger, ex);
            await reporter.WarnAsync(Crashed, cancellationToken);
        }
    }
}
