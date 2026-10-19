using System.Collections.ObjectModel;
using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies;
using XDrishti.Strategies.BuiltIn;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// The built-in strategies a run can name by id (spec P3-04 Scope 2, Hand-over; table-driven since P3-04b2, D3): until stored strategies exist (P3-02c, P3-02d) there is no text to give, so a request names an id and its parameters and gets the
/// definition of the table's builder (<c>ORB</c>: <see cref="OrbV1"/>). The factory knows a closed list of ids and, per id, a closed list of parameter names with their defaults; a name it does not know is refused with a located problem, never
/// ignored. The values are not judged here: the compile of <see cref="Running.Simulator.Create"/> checks their ranges. A later strategy is one more row of <see cref="Table"/>.
/// </summary>
public static class BuiltInStrategies
{
    /// <summary>The parameter of <c>ORB</c> that is the target as a multiple of the risk (<c>FIXED_RR</c>'s <c>rr</c>).</summary>
    public const string RewardRiskParam = "rr";

    private const double DefaultOrMinutes = 15;

    private const double DefaultRewardRisk = 2.0;

    private const double DefaultPullAtr = 0.5;

    private const double MaxPullAtr = 3;

    private const double DefaultBaseLen = 20;

    private const double DefaultBasePct = 8;

    private const double DefaultVolX = 1.5;

    private const double MaxRewardRisk = 10;

    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    private static readonly BuiltInEntry[] Table =
    [
        new BuiltInEntry(
            OrbV1.Id,
            "Opening range breakout: a close beyond the opening range, stop at the opposite end, target a multiple of the risk",
            [Timeframe.M5, Timeframe.M15],
            Timeframe.M15,
            [
                new BuiltInParameterInfo(OrbV1.OrMinutesParam, DefaultOrMinutes, "minutes of the opening range"),
                new BuiltInParameterInfo(RewardRiskParam, DefaultRewardRisk, "target as a multiple of the risk"),
            ],
            values => OrbV1.Create(values[OrbV1.OrMinutesParam], values[RewardRiskParam])),
        new BuiltInEntry(
            VwapPullback.Id,
            "VWAP pullback: after two closes beyond VWAP, a dip to within pull_atr ATRs of it and a close back through VWAP and the open; stop at the pullback extreme, target a multiple of the risk",
            [Timeframe.M15],
            Timeframe.M15,
            [
                new BuiltInParameterInfo(VwapPullback.PullAtrParam, DefaultPullAtr, "distance of the dip's extreme from VWAP, in ATRs", Min: 0, Max: MaxPullAtr, MinExclusive: true),
                new BuiltInParameterInfo(RewardRiskParam, DefaultRewardRisk, "target as a multiple of the risk"),
            ],
            values => VwapPullback.Create(values[VwapPullback.PullAtrParam], values[RewardRiskParam])),
        new BuiltInEntry(
            BaseBreakoutV1.Id,
            "Base breakout: a new closing high out of a tight base, in an uptrend, on rising volume; entered at the next open, held at most five sessions",
            [Timeframe.D1],
            Timeframe.D1,
            [
                new BuiltInParameterInfo(BaseBreakoutV1.BaseLenParam, DefaultBaseLen, "sessions in the base window, also the lookback of the stop", Min: 10, Max: 60),
                new BuiltInParameterInfo(BaseBreakoutV1.BasePctParam, DefaultBasePct, "the widest span of the closes of the window, as % of the lowest", Min: 2, Max: 20),
                new BuiltInParameterInfo(BaseBreakoutV1.VolXParam, DefaultVolX, "the least multiple of the volume of the 20 sessions before", Min: 1, Max: 5),
                new BuiltInParameterInfo(RewardRiskParam, DefaultRewardRisk, "target as a multiple of the risk", Min: 0, Max: MaxRewardRisk, MinExclusive: true),
            ],
            values => BaseBreakoutV1.Create(values[BaseBreakoutV1.BaseLenParam], values[BaseBreakoutV1.BasePctParam], values[BaseBreakoutV1.VolXParam], values[RewardRiskParam])),
    ];

    /// <summary>The ids of the built-in strategies, in the order of the table.</summary>
    public static IReadOnlyList<string> Ids { get; } = Array.AsReadOnly([.. Table.Select(e => e.Id)]);

    /// <summary>The built-in strategies with their timeframes and parameters (the table as values).</summary>
    public static IReadOnlyList<BuiltInStrategyInfo> Catalog { get; } = Array.AsReadOnly([.. Table.Select(e => e.ToInfo())]);

    /// <summary>Builds the definition of a built-in strategy from its id and parameters (null or empty: the defaults).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="strategyId"/> is null.</exception>
    public static BuiltInBuild Build(string strategyId, IReadOnlyDictionary<string, double>? parameters)
    {
        ArgumentNullException.ThrowIfNull(strategyId);
        var entry = Table.FirstOrDefault(e => string.Equals(e.Id, strategyId, StringComparison.Ordinal));
        if (entry is null)
        {
            var known = string.Join(", ", Ids.Select(i => "`" + i + "`"));
            return Refused("strategy_id", SimulationErrors.UnknownStrategyCode, "The strategy `" + strategyId + "` is not a built-in one; the built-in ids are " + known + ".");
        }

        var given = parameters ?? new Dictionary<string, double>();
        var names = entry.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<LocatedProblem>();
        foreach (var name in given.Keys.Order(StringComparer.Ordinal))
        {
            if (!names.Contains(name))
            {
                problems.Add(new LocatedProblem(
                    "parameters." + name,
                    new Problem(SimulationErrors.UnknownParameterCode, "The strategy `" + entry.Id + "` has no parameter `" + name + "`; its parameters are " + NameList(entry) + ".", NoText)));
            }
        }

        if (problems.Count == 0)
        {
            // a declared range judges finite values only: a NaN or an infinity is neither in nor out of it and keeps the existing refusal of StrategyPreparation (ADR 0041 Amendment 2)
            foreach (var parameter in entry.Parameters.Where(p => p.HasRange))
            {
                if (given.TryGetValue(parameter.Name, out var value) && double.IsFinite(value) && !parameter.Contains(value))
                {
                    problems.Add(new LocatedProblem(
                        "parameters." + parameter.Name,
                        new Problem(SimulationErrors.ParameterOutOfRangeCode, "The parameter `" + parameter.Name + "` of `" + entry.Id + "` must satisfy `" + parameter.RangeText() + "`; " + value.ToString("R", CultureInfo.InvariantCulture) + " does not.", NoText)));
                }
            }
        }

        if (problems.Count > 0)
        {
            return new BuiltInBuild(null, new ReadOnlyDictionary<string, double>(new Dictionary<string, double>()), problems.AsReadOnly());
        }

        var effective = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in entry.Parameters)
        {
            effective[parameter.Name] = given.TryGetValue(parameter.Name, out var value) ? value : parameter.Default;
        }

        return new BuiltInBuild(entry.Build(effective), new ReadOnlyDictionary<string, double>(effective), []);
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

    /// <summary>The parameter names as prose: <c>`a` and `b`</c>, <c>`a`, `b` and `c`</c>.</summary>
    private static string NameList(BuiltInEntry entry)
    {
        var quoted = entry.Parameters.Select(p => "`" + p.Name + "`").ToList();
        return quoted.Count == 1 ? quoted[0] : string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[^1];
    }

    private static BuiltInBuild Refused(string path, string code, string message) =>
        new(null, new ReadOnlyDictionary<string, double>(new Dictionary<string, double>()), new[] { new LocatedProblem(path, new Problem(code, message, NoText)) }.AsReadOnly());
}
