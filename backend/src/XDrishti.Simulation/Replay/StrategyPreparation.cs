using System.Collections.ObjectModel;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// Makes a <see cref="PreparedStrategy"/> from a definition (spec P3-04, Scope 2): it calls <see cref="Simulator.Create"/>, so it refuses what the simulator refuses and the located problems pass through unchanged, and builds the
/// <see cref="StrategyRecord"/> with the canonical JSON of the definition (<see cref="DefinitionJson"/>) and its SHA-256. The parameters are the ones the caller built the definition with: kept as given, in ordinal name order, and not
/// interpreted.
/// </summary>
public static class StrategyPreparation
{
    /// <summary>Prepares the strategy for a run.</summary>
    /// <param name="definition">The strategy.</param>
    /// <param name="parameters">The parameters the definition was built with (effective values), recorded in the run and in its key.</param>
    /// <param name="evaluationTimeframe">The timeframe the rules are evaluated on.</param>
    /// <param name="fills">The fill rules of the walks; null means <see cref="FillRules.Default"/> (P3-03b).</param>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> or <paramref name="parameters"/> is null.</exception>
    /// <exception cref="ArgumentException">A parameter value is not a finite number.</exception>
    public static PreparedBuild Prepare(StrategyDefinition definition, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var (name, value) in parameters)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("The parameter `" + name + "` is not a finite number.", nameof(parameters));
            }
        }

        var build = Simulator.Create(definition, evaluationTimeframe, fills);
        if (build.Simulator is not { } simulator)
        {
            return new PreparedBuild(null, build.Problems);
        }

        var json = DefinitionJson.Write(definition);
        var ordered = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            ordered[name] = value;
        }

        var record = new StrategyRecord(definition.Id, new ReadOnlyDictionary<string, double>(ordered), json, CanonicalText.Sha256Hex(json), evaluationTimeframe, simulator.Sides);
        return new PreparedBuild(new PreparedStrategy(simulator, record, build.Problems), build.Problems);
    }
}
