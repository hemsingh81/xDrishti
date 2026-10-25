using XDrishti.Indicators;

namespace XDrishti.Strategies.Binding;

/// <summary>
/// A catalog feature read from the frame: the <see cref="Spec"/> in canonical form (the same value for every spelling of one feature, so that the frame shares one instance) and the <see cref="Lag"/> in closed bars
/// of the feature's own timeframe (0 for the current value, <c>close[2]</c> is 2). <see cref="Type"/> is <see cref="BoundType.Regime"/> for <c>regime()</c> and <see cref="BoundType.Number"/> for every other feature.
/// <see cref="IsReference"/> is true when it was written as a reference (<c>close</c>, <c>prev_day.high</c>, <c>tf("1h").close</c>) and not as a call (<c>rsi(14)</c>); since ADR 0108 either can be given a subscript.
/// </summary>
internal sealed record BoundFeature(FeatureSpec Spec, int Lag, BoundType Type, bool IsReference, SourceSpan Span) : IBoundExpression;
