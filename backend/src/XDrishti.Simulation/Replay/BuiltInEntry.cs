using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// One row of the table of built-in strategies (spec P3-04b2, D3): the id, a description, the timeframes and the default one, the parameters with their defaults, and the builder that turns the effective values (every parameter present,
/// defaults filled in) into the definition. P3-05 and P3-06 add one row each; the factory, the catalog and the verb read the table and do not change.
/// </summary>
internal sealed record BuiltInEntry(
    string Id,
    string Description,
    IReadOnlyList<Timeframe> AllowedTimeframes,
    Timeframe DefaultTimeframe,
    IReadOnlyList<BuiltInParameterInfo> Parameters,
    Func<IReadOnlyDictionary<string, double>, StrategyDefinition> Build)
{
    /// <summary>The entry as a value for the port.</summary>
    public BuiltInStrategyInfo ToInfo() => new(Id, Description, AllowedTimeframes, DefaultTimeframe, Parameters);
}
