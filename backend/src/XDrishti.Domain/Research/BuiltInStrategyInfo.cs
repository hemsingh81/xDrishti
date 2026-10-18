using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// One built-in strategy as the catalog lists it (spec P3-04b2, D3): the id a run names, a one-line description, the timeframes it can be evaluated on, the one a run uses when none is given, and its parameters with their defaults.
/// A value only; the engine behind <c>ISimulationEngine</c> builds the list, the verb prints it and reads the default timeframe.
/// </summary>
public sealed record BuiltInStrategyInfo(
    string Id,
    string Description,
    IReadOnlyList<Timeframe> Timeframes,
    Timeframe DefaultTimeframe,
    IReadOnlyList<BuiltInParameterInfo> Parameters);
