using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>Why a trade was entered: the conditions of its side at the decision bar, as the engine that made the trade saw them (P3-04d). Not stored in the run.</summary>
/// <param name="DecisionEpochSeconds">The decision instant (the end of the minute that closed the evaluation bar).</param>
/// <param name="Timeframe">The evaluation timeframe.</param>
/// <param name="Sections">The lists that exist, in the order setup, trigger, invalidate.</param>
public sealed record TradeExplanation(long DecisionEpochSeconds, Timeframe Timeframe, IReadOnlyList<ConditionSectionTrace> Sections);
