using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions.Rules;

/// <summary>
/// What the rules read (ADR 0022): the open holdings with all their lots, the India date, the thresholds, the master's sector of each instrument and the instruments that are in at least one live basket. Plain data: the rules do no I/O and read no clock.
/// <see cref="Returns"/> memoises the returns of a holding for the one evaluation this context is made for (the XIRR solve is the expensive part, and four rules read the same figures): a context is used by one evaluation on one thread.
/// </summary>
internal sealed class ActionContext(
    IReadOnlyList<HoldingData> holdings,
    DateOnly today,
    ActionThresholds thresholds,
    IReadOnlyDictionary<Guid, string> sectors,
    IReadOnlySet<Guid> inBasket)
{
    private readonly Dictionary<Guid, ReturnFigures> _returns = [];

    public IReadOnlyList<HoldingData> Holdings { get; } = holdings;

    public DateOnly Today { get; } = today;

    public ActionThresholds Thresholds { get; } = thresholds;

    public IReadOnlyDictionary<Guid, string> Sectors { get; } = sectors;

    public IReadOnlySet<Guid> InBasket { get; } = inBasket;

    /// <summary>The returns of the holding on <see cref="Today"/> (<see cref="ReturnsCalculator.ForHolding"/>), computed once per holding.</summary>
    public ReturnFigures Returns(HoldingData holding)
    {
        ArgumentNullException.ThrowIfNull(holding);
        if (!_returns.TryGetValue(holding.Id, out var figures))
        {
            _returns[holding.Id] = figures = ReturnsCalculator.ForHolding(holding, Today);
        }

        return figures;
    }
}
