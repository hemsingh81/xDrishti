using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The baskets a strategy was tested on and what they add to the multiple-testing factor (BK-11).</summary>
/// <param name="StrategyId">The strategy family.</param>
/// <param name="Baskets">Distinct baskets tested.</param>
/// <param name="Tests">Distinct tests (strategy hash, basket, window).</param>
/// <param name="ExtraVariants">Baskets minus one.</param>
/// <param name="Factor">The factor of that many variants.</param>
/// <param name="Notice">Where the number comes from.</param>
public sealed record UniverseTrialsView(string StrategyId, int Baskets, int Tests, int ExtraVariants, double Factor, string Notice)
{
    /// <summary>Printed with the figures.</summary>
    public const string NoticeText = "Counted from the stored Lab tests, not from memory: k distinct baskets are k variants (k - 1 added to the one every strategy has); a repeated test of the same version, basket and window counts once. The honest count of looks (ADR 0207) is the number the verdicts use when the owner's switch is on.";
}

/// <summary>Reads the universe trial count of a strategy.</summary>
public sealed record GetUniverseTrialsQuery(string StrategyId) : IQuery<UniverseTrialsView>;

internal sealed class GetUniverseTrialsHandler(IUniverseTrialSource source) : IQueryHandler<GetUniverseTrialsQuery, UniverseTrialsView>
{
    public async ValueTask<UniverseTrialsView> HandleAsync(GetUniverseTrialsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var count = UniverseTrials.Count(await source.ListBasketTestsAsync(query.StrategyId, cancellationToken));
        return new UniverseTrialsView(query.StrategyId, count.Baskets, count.Tests, count.ExtraVariants, count.Factor, UniverseTrialsView.NoticeText);
    }
}
