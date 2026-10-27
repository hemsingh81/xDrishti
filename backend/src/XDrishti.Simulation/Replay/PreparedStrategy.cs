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
    internal PreparedStrategy(Simulator simulator, PreparedMember member, IReadOnlyList<LocatedProblem> warnings)
    {
        Simulator = simulator;
        Record = member.Record;
        Warnings = warnings;
        Members = new[] { member }.AsReadOnly();
    }

    /// <summary>A comparison (P4-05): one scan, one member per candidate exit; the simulator of the first member stands for the properties the members share.</summary>
    internal PreparedStrategy(SimulatorComparison comparison, IReadOnlyList<PreparedMember> members, IReadOnlyList<LocatedProblem> warnings)
    {
        Comparison = comparison;
        Simulator = comparison.Members[0];
        Record = members[0].Record;
        Warnings = warnings;
        Members = members;
    }

    /// <summary>The candidates, in order: one for a plain run or a single chosen exit, two or more for a comparison. Each has the record its run keeps (the definition with that exit, so the run keys differ).</summary>
    public IReadOnlyList<PreparedMember> Members { get; }

    /// <summary>The strategy as a run records it (of the first candidate for a comparison): id, parameters, canonical JSON and its SHA-256, timeframe and sides.</summary>
    public StrategyRecord Record { get; }

    /// <summary>The warnings of the compile (the problems of <see cref="Simulator.Create"/> when it succeeded).</summary>
    public IReadOnlyList<LocatedProblem> Warnings { get; }

    /// <summary>The fill rules every walk of this strategy applies (<see cref="FillRules.Default"/> when <see cref="StrategyPreparation.Prepare"/> was given none).</summary>
    public FillRules Fills => Simulator.Fills;

    /// <summary>The strategy reads the daily market context: the replay needs the stored days and skips with <c>context_unavailable</c> when there are none (P3-lift, ADR 0145).</summary>
    public bool ReadsContext => Simulator.ReadsContext;

    internal Simulator Simulator { get; }

    /// <summary>The strategy is a swing one: its positions are held across sessions and a run gates them (a comparison does not, P4-05 D4).</summary>
    public bool HoldsOvernight => Simulator.HoldsOvernight;

    /// <summary>The one scan with N walks; null for a plain run.</summary>
    internal SimulatorComparison? Comparison { get; }
}
