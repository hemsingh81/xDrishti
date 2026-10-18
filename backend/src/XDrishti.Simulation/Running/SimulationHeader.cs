using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Running;

/// <summary>
/// What a result says it is (spec P3-03a, D11; ADR 0037, amendment 1): the version of the rules that made it, the schema of its trades, the strategy, how the fills and the costs were obtained, and five labels in a fixed
/// order. A figure from this engine is a signal-level result per instrument on invented data and says nothing about edge.
/// </summary>
/// <param name="EngineVersion"><see cref="SimulationEngine.Version"/> of the engine that ran.</param>
/// <param name="SchemaVersion"><see cref="SimTrade.CurrentSchemaVersion"/> of the trades.</param>
/// <param name="StrategyId">The strategy that decided.</param>
/// <param name="Fills">How the prices were obtained.</param>
/// <param name="Costs">Whether charges were taken off.</param>
/// <param name="Labels">The five labels, in order: fills, entry limit, costs, portfolio limits, sample.</param>
public sealed record SimulationHeader(string EngineVersion, int SchemaVersion, string StrategyId, FillBasis Fills, CostBasis Costs, IReadOnlyList<string> Labels)
{
    /// <summary>The five labels of this engine (D11); P3-03b replaces the first two with the description of its fill rules when it merges.</summary>
    public static IReadOnlyList<string> StandardLabels { get; } =
        Array.AsReadOnly(["fills: exact", "entry limit: not applied", "costs: none", "portfolio limits: not simulated", "sample: in-sample"]);

    /// <summary>Value equality: every field, and the labels in order.</summary>
    public bool Equals(SimulationHeader? other) =>
        other is not null
        && EngineVersion == other.EngineVersion && SchemaVersion == other.SchemaVersion && StrategyId == other.StrategyId && Fills == other.Fills && Costs == other.Costs
        && Labels.SequenceEqual(other.Labels);

    /// <summary>A hash consistent with <see cref="Equals(SimulationHeader?)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(EngineVersion, SchemaVersion, StrategyId, Fills, Costs, Labels.Count);
}
