using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Replay;

/// <summary>What <see cref="StrategyPreparation.Prepare"/> returns, as <see cref="Running.SimulatorBuild"/> does: the prepared strategy, or no strategy and the located problems that stopped it (errors first). When a strategy exists, the problems are the warnings of the compile.</summary>
public sealed record PreparedBuild(PreparedStrategy? Strategy, IReadOnlyList<LocatedProblem> Problems);
