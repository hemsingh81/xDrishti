using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// A strategy ready to be replayed: the record a run keeps of it (<see cref="Record"/>), the warnings of its compile and the simulator that holds the compiled sides (internal). Immutable and safe to share between threads: every
/// replay makes its own frames and scanners. Only <see cref="StrategyPreparation.Prepare"/> makes one.
/// </summary>
public sealed class PreparedStrategy
{
    internal PreparedStrategy(Simulator simulator, StrategyRecord record, IReadOnlyList<LocatedProblem> warnings)
    {
        Simulator = simulator;
        Record = record;
        Warnings = warnings;
    }

    /// <summary>The strategy as a run records it: id, parameters, canonical JSON and its SHA-256, timeframe and sides.</summary>
    public StrategyRecord Record { get; }

    /// <summary>The warnings of the compile (the problems of <see cref="Simulator.Create"/> when it succeeded).</summary>
    public IReadOnlyList<LocatedProblem> Warnings { get; }

    /// <summary>The fill rules every walk of this strategy applies (<see cref="FillRules.Default"/> when <see cref="StrategyPreparation.Prepare"/> was given none).</summary>
    public FillRules Fills => Simulator.Fills;

    internal Simulator Simulator { get; }
}
