using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Running;

/// <summary>
/// What a result says it is (spec P3-03a, D11; ADR 0037, amendment 1): the version of the rules that made it, the schema of its trades, the strategy, how the fills and the costs were obtained, and five labels in a fixed
/// order. A figure from this engine is a signal-level result per instrument on invented data and says nothing about edge.
/// </summary>
/// <param name="EngineVersion"><see cref="SimulationEngine.Version"/> of the engine that ran.</param>
/// <param name="SchemaVersion"><see cref="SimTrade.CurrentSchemaVersion"/> of the trades.</param>
/// <param name="StrategyId">The strategy that decided.</param>
/// <param name="FillRules">The entry limit and the slippage the walks applied (ADR 0038).</param>
/// <param name="Fills">How the prices were obtained: <see cref="XDrishti.Domain.Research.FillRules.Basis"/> of <paramref name="FillRules"/>.</param>
/// <param name="Costs">Whether charges were taken off: always <see cref="CostBasis.None"/> here. The replay applies costs after the walk, and a run's labels come from the run record (ADR 0042, D1).</param>
/// <param name="Labels">The five labels, in order: fills, entry limit (both from <see cref="FillDescription.Labels"/>), costs, portfolio limits, sample.</param>
public sealed record SimulationHeader(string EngineVersion, int SchemaVersion, string StrategyId, FillRules FillRules, FillBasis Fills, CostBasis Costs, IReadOnlyList<string> Labels)
{
    /// <summary>The last three labels of this engine (D11), which do not depend on the fill rules.</summary>
    public static IReadOnlyList<string> FixedLabels => RunLabels.FixedLabels;

    /// <summary>The five labels for these fill rules of a gross result: the two of <see cref="FillDescription.Labels"/>, then <see cref="FixedLabels"/>. Equal to <see cref="LabelsOf(FillRules, CostRecord?)"/> with no costs.</summary>
    public static IReadOnlyList<string> LabelsOf(FillRules rules) => LabelsOf(rules, null);

    /// <summary>
    /// The five labels in the same order (fills, entry limit, costs, portfolio limits, sample), the third from <see cref="CostDescription.Label"/> of what the run charged with (ADR 0042, D15): <c>costs: none</c> for a gross run. A run's labels
    /// come from the run record; this header describes the gross result of <see cref="Simulator.Run"/>.
    /// </summary>
    public static IReadOnlyList<string> LabelsOf(FillRules rules, CostRecord? costs) => Array.AsReadOnly([.. FillDescription.Labels(rules), CostDescription.Label(costs), FixedLabels[1], FixedLabels[2]]);

    /// <summary>Value equality: every field, and the labels in order.</summary>
    public bool Equals(SimulationHeader? other) =>
        other is not null
        && EngineVersion == other.EngineVersion && SchemaVersion == other.SchemaVersion && StrategyId == other.StrategyId && FillRules == other.FillRules && Fills == other.Fills && Costs == other.Costs
        && Labels.SequenceEqual(other.Labels);

    /// <summary>A hash consistent with <see cref="Equals(SimulationHeader?)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(EngineVersion, SchemaVersion, StrategyId, FillRules, Fills, Costs, Labels.Count);
}
