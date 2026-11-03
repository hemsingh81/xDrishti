using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// What the pair walker needs (P14-13): the selected pair with the formation's hedge ratio, the 1-minute bars of both legs (ascending, raw; they may start some sessions before <paramref name="From"/> so that the z-score has its look-back), the ticks, the fill rules, the variant, the notional,
/// and the trading dates. Charges are not the walker's business (see <see cref="PairCosting"/>).
/// </summary>
public sealed record PairWalkInput(
    SelectedPair Pair,
    IReadOnlyList<Bar> YBars,
    IReadOnlyList<Bar> XBars,
    TickSpec YTick,
    TickSpec XTick,
    PairVariant Variant,
    PairStudyOptions Options,
    FillRules Fills,
    DateOnly From,
    DateOnly To);
