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

    private static ShadowScorer Build(IModelRegistry registry, bool throws)
    {
        if (throws)
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
