using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The stop of a compiled side: the kind, the timeframe it is read on (<c>Timeframe</c>; the evaluation timeframe when the model gave none), the level feature (<c>lowest(5, source=low)</c>, <c>or_low(15)</c>, <c>low</c>) and
/// the ATR feature (<c>atr(14)</c>) the simulator reads on that timeframe, the percentage or multiple of a kind that has one, and the buffer in ATRs (0 when none). The formula of the stop price is the simulator's.
/// </summary>
public sealed record StopPlan(StopKind Kind, Timeframe Timeframe, FeatureSpec? Level, FeatureSpec? Atr, double? Pct, double? Mult, double BufferAtr);
