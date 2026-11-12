using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>A <see cref="ShadowScorer"/> over fake ports; <see cref="Empty"/> has no model, so it scores nothing.</summary>
internal static class ShadowScorerFake
{
    public static ShadowScorer Empty() => Build(Substitute.For<IModelRegistry>(), throws: false);

    /// <summary>A scorer whose model registry throws, as a failing store would.</summary>
    public static ShadowScorer Throwing() => Build(Substitute.For<IModelRegistry>(), throws: true);

    /// <summary>A scorer that signals <paramref name="started"/> and then waits until the token it is given is cancelled.</summary>
    public static ShadowScorer Blocking(TaskCompletionSource started)
    {
        var registry = Substitute.For<IModelRegistry>();
#pragma warning disable CA2012 // NSubstitute configures the substitute with the ValueTask-returning call; it is never awaited as a result
        registry.ListAsync(Arg.Any<XDrishti.Domain.Learning.ModelRole?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => WaitAsync(started, call.ArgAt<CancellationToken>(2)));
#pragma warning restore CA2012
        return Build(registry, throws: null);
    }

    private static async ValueTask<IReadOnlyList<StoredModel>> WaitAsync(TaskCompletionSource started, CancellationToken token)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        return [];
    }

    private static ShadowScorer Build(IModelRegistry registry, bool? throws)
    {
        if (throws is null)
        {
        }
        else if (throws is true)
        {
            registry.ListAsync(Arg.Any<XDrishti.Domain.Learning.ModelRole?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<StoredModel>>(_ => throw new InvalidOperationException("registry down"));
        }
        else
        {
            registry.ListAsync(Arg.Any<XDrishti.Domain.Learning.ModelRole?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<StoredModel>)[]);
        }

        return new ShadowScorer(
            registry, Substitute.For<IModelFiles>(), Substitute.For<IModelScorerLoader>(), Substitute.For<IBasketQueries>(), Substitute.For<IBarStore>(), Substitute.For<IBackfillSeams>(),
            Substitute.For<IAdjustmentFactors>(), Substitute.For<ITradeFeatureExtractor>(), Substitute.For<ICellStatsStore>(), TimeProvider.System, NullLogger<ShadowScorer>.Instance);
    }
}
