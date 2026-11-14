using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Strength;

namespace XDrishti.Application.Market.Context;

/// <summary>After a data job that aggregated, writes the context of the gate basket (BK-08) and the relative strength of the sessions that have none (BK-09). Never fails the job: whatever goes wrong is a warning of the job. The NIFTY 50 / INDIA VIX context that this step also ran was dropped (ADR 0292).</summary>
internal sealed class BasketUpkeep(
    ILogger<BasketUpkeep> logger,
    ICommandHandler<ComputeBasketContextCommand, BasketContextComputed>? basketContext = null,
    ICommandHandler<ComputeRelativeStrengthCommand, RelativeStrengthComputed>? strength = null) : IBasketUpkeep
{
    private const string Crashed = "The basket context could not be updated. See the API log.";

    public async ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        try
        {
            // BK-08: the daily context of the gate basket, when the owner chose one; insert-once, so a failure only leaves the gate reading unavailable.
            if (basketContext is not null)
            {
                var basket = await basketContext.HandleAsync(new ComputeBasketContextCommand(), cancellationToken);
                if (basket.IsFailure)
                {
                    await reporter.WarnAsync("Basket context: " + basket.Error.Message, cancellationToken);
                }
            }

            // The relative strength of the sessions that have none (BK-09): insert-once, OFF by default; a failure is a warning of its own and the rows of earlier days stay.
            if (strength is not null && await strength.HandleAsync(new ComputeRelativeStrengthCommand(), cancellationToken) is { IsFailure: true } ranked)
            {
                await reporter.WarnAsync("Relative strength: " + ranked.Error.Message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the upkeep is a side effect of the job: whatever goes wrong is a warning, the job's own result stands
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ContextLog.UpkeepCrashed(logger, ex);
            await reporter.WarnAsync(Crashed, cancellationToken);
        }
    }
}
