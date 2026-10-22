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
    /// <param name="candidateId">The id of the exit when one was chosen (P4-05); null names the member by the strategy id.</param>
    /// <param name="exit">The exit when one was chosen; null for the built-in's own.</param>
    public static PreparedBuild Prepare(StrategyDefinition definition, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills = null, string? candidateId = null, ExitCandidate? exit = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        CheckParameters(parameters);

        var build = Simulator.Create(definition, evaluationTimeframe, fills);
        if (build.Simulator is not { } simulator)
        {
            return new PreparedBuild(null, build.Problems);
        }

        var member = MemberOf(candidateId ?? definition.Id, exit, definition, parameters, evaluationTimeframe, simulator);
        return new PreparedBuild(new PreparedStrategy(simulator, member, build.Problems), build.Problems);
    }

    /// <summary>
    /// Prepares a comparison (P4-05, D2): <paramref name="members"/> is the strategy once per candidate with its exit alone, <paramref name="ids"/> their ids. The record of each member is
    /// the one a run of that candidate alone would keep, so its run key names the exit.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A parameter value is not a finite number, or the ids and the members differ in number.</exception>
    public static PreparedBuild PrepareComparison(IReadOnlyList<StrategyDefinition> members, IReadOnlyList<string> ids, IReadOnlyList<ExitCandidate> exits, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(exits);
        if (ids.Count != members.Count || exits.Count != members.Count)
        {
            throw new ArgumentException("One id for each candidate.", nameof(ids));
        }

        CheckParameters(parameters);
        var build = SimulatorComparison.Create(members, evaluationTimeframe, fills);
        if (build.Comparison is not { } comparison)
        {
            return new PreparedBuild(null, build.Problems);
        }

        var prepared = new List<PreparedMember>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            prepared.Add(MemberOf(ids[i], exits[i], members[i], parameters, evaluationTimeframe, comparison.Members[i]));
        }

        return new PreparedBuild(new PreparedStrategy(comparison, prepared.AsReadOnly(), build.Problems), build.Problems);
    }

    private static void CheckParameters(IReadOnlyDictionary<string, double> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var (name, value) in parameters)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("The parameter `" + name + "` is not a finite number.", nameof(parameters));
            }
        }
    }

    private static PreparedMember MemberOf(string id, ExitCandidate? exit, StrategyDefinition definition, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, Simulator simulator)
    {
        var json = DefinitionJson.Write(definition);
        var ordered = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            ordered[name] = value;
        }

        var record = new StrategyRecord(definition.Id, new ReadOnlyDictionary<string, double>(ordered), json, CanonicalText.Sha256Hex(json), evaluationTimeframe, simulator.Sides);
        return new PreparedMember(id, exit, record, CanonicalText.Sha256Hex(DefinitionJson.Write(WithoutExits(definition))));
    }

    /// <summary>The definition with the exits and the overlays removed, on both sides: what fixes the entries (P4-05, D10).</summary>
    private static StrategyDefinition WithoutExits(StrategyDefinition definition) => definition with
    {
        Buy = definition.Buy is null ? null : definition.Buy with { Exits = null, Overlays = null },
        Sell = definition.Sell is null ? null : definition.Sell with { Exits = null, Overlays = null },
    };
}
