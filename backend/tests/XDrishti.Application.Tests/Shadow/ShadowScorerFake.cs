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
    public static ShadowScorer Empty()
    {
        var registry = Substitute.For<IModelRegistry>();
        registry.ListAsync(Arg.Any<XDrishti.Domain.Learning.ModelRole?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<StoredModel>)[]);
        return new ShadowScorer(
            registry, Substitute.For<IModelFiles>(), Substitute.For<IModelScorerLoader>(), Substitute.For<IBasketQueries>(), Substitute.For<IBarStore>(), Substitute.For<IBackfillSeams>(),
            Substitute.For<IAdjustmentFactors>(), Substitute.For<ITradeFeatureExtractor>(), Substitute.For<ICellStatsStore>(), NullLogger<ShadowScorer>.Instance);
    }
}
