using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>Stands in for the basket upkeep of a data job and records that it ran.</summary>
internal sealed class FakeBasketUpkeep : IBasketUpkeep
{
    public int Runs { get; private set; }

    public ValueTask RunAsync(JobReporter reporter, CancellationToken cancellationToken)
    {
        Runs++;
        return ValueTask.CompletedTask;
    }
}
