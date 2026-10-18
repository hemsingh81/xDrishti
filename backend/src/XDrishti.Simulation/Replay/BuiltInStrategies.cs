using System.Collections.ObjectModel;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// The built-in strategies a run can name by id (spec P3-04 Scope 2, Hand-over): until stored strategies exist (P3-02c, P3-02d) there is no text to give, so a request names <c>ORB</c> and its parameters and gets the definition of
/// <see cref="OrbV1"/>. The factory knows a closed list of ids and, per id, a closed list of parameter names with their defaults; a name it does not know is refused with a located problem, never ignored. The values are not judged
/// here: the compile of <see cref="Running.Simulator.Create"/> checks their ranges.
/// </summary>
public static class BuiltInStrategies
{
    /// <summary>The parameter of <c>ORB</c> that is the target as a multiple of the risk (<c>FIXED_RR</c>'s <c>rr</c>).</summary>
    public const string RewardRiskParam = "rr";

    private const double DefaultOrMinutes = 15;

    private const double DefaultRewardRisk = 2.0;

    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    /// <summary>The ids of the built-in strategies.</summary>
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly([OrbV1.Id]);

    /// <summary>Builds the definition of a built-in strategy from its id and parameters (null or empty: the defaults).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="strategyId"/> is null.</exception>
    public static BuiltInBuild Build(string strategyId, IReadOnlyDictionary<string, double>? parameters)
    {
        ArgumentNullException.ThrowIfNull(strategyId);
        if (!string.Equals(strategyId, OrbV1.Id, StringComparison.Ordinal))
        {
            var known = string.Join(", ", Ids.Select(i => "`" + i + "`"));
            return Refused("strategy_id", SimulationErrors.UnknownStrategyCode, "The strategy `" + strategyId + "` is not a built-in one; the built-in ids are " + known + ".");
        }

        var given = parameters ?? new Dictionary<string, double>();
        var problems = new List<LocatedProblem>();
        foreach (var name in given.Keys.Order(StringComparer.Ordinal))
        {
            if (name is not (OrbV1.OrMinutesParam or RewardRiskParam))
            {
                problems.Add(new LocatedProblem(
                    "parameters." + name,
                    new Problem(SimulationErrors.UnknownParameterCode, "The strategy `ORB` has no parameter `" + name + "`; its parameters are `" + OrbV1.OrMinutesParam + "` and `" + RewardRiskParam + "`.", NoText)));
            }
        }

        if (problems.Count > 0)
        {
            return new BuiltInBuild(null, new ReadOnlyDictionary<string, double>(new Dictionary<string, double>()), problems.AsReadOnly());
        }

        var orMinutes = given.TryGetValue(OrbV1.OrMinutesParam, out var minutes) ? minutes : DefaultOrMinutes;
        var rewardRisk = given.TryGetValue(RewardRiskParam, out var rr) ? rr : DefaultRewardRisk;
        var effective = new SortedDictionary<string, double>(StringComparer.Ordinal) { [OrbV1.OrMinutesParam] = orMinutes, [RewardRiskParam] = rewardRisk };
        return new BuiltInBuild(OrbV1.Create(orMinutes, rewardRisk), new ReadOnlyDictionary<string, double>(effective), []);
    }

    /// <summary>
    /// Builds the definition of a built-in strategy and prepares it in one step (<see cref="Build"/>, then <see cref="StrategyPreparation.Prepare"/>), so that a caller that only knows ids and numbers (the engine adapter of P3-04a) names no type of the strategy model.
    /// The problems are those of the factory when it refused the id or a parameter name, otherwise those of the compile. The parameters recorded are the effective ones (the defaults filled in).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="strategyId"/> is null.</exception>
    /// <exception cref="ArgumentException">A parameter value is not a finite number.</exception>
    public static PreparedBuild Prepare(string strategyId, IReadOnlyDictionary<string, double>? parameters, Timeframe evaluationTimeframe, FillRules? fills = null)
    {
        var built = Build(strategyId, parameters);
        return built.Definition is { } definition
            ? StrategyPreparation.Prepare(definition, built.Parameters, evaluationTimeframe, fills)
            : new PreparedBuild(null, built.Problems);
    }

    private static BuiltInBuild Refused(string path, string code, string message) =>
        new(null, new ReadOnlyDictionary<string, double>(new Dictionary<string, double>()), new[] { new LocatedProblem(path, new Problem(code, message, NoText)) }.AsReadOnly());
}
