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

    /// <summary>The strategy names the filter <c>not_event_day</c>: the replay needs the results-calendar snapshot of the run and skips with <c>event_calendar_unavailable</c> when it has none (P15-06, ADR 0182).</summary>
    public bool ReadsEvents => Simulator.ReadsEvents;

    /// <summary>The strategy names the filter <c>min_turnover</c> (CF-05, ADR 0230): the fill rules carry its threshold and the replay needs the turnover lookup; without it every entry is blocked and counted.</summary>
    public bool ReadsTurnover => Simulator.ReadsTurnover;

    /// <summary>The strategy names <c>days_to_results</c>, <c>days_since_results</c> or lists <c>event_exit</c> (P15-09): the replay needs the results-days inputs of the run and skips with <c>event_calendar_unavailable</c> when it has none (P15-05, ADR 0185).</summary>
    public bool ReadsResultsDays => Simulator.ReadsResultsDays;

    /// <summary>The strategy names <c>results_sue_q</c> or <c>results_car3_q</c> (P15-09): the replay needs the surprise numbers of the run and skips with <c>event_calendar_unavailable</c> when it has none.</summary>
    public bool ReadsSurprise => Simulator.ReadsSurprise;

    /// <summary>The strategy names <c>mom_rank</c>, <c>rs_vs_index</c> or <c>sector_rs</c> (BK-09): the replay needs the stored rows of the instrument and skips with <c>event_calendar_unavailable</c> when it has none.</summary>
    public bool ReadsRelativeStrength => Simulator.ReadsRelativeStrength;

    internal Simulator Simulator { get; }

    /// <summary>The strategy is a swing one: its positions are held across sessions and a run gates them (a comparison does not, P4-05 D4).</summary>
    public bool HoldsOvernight => Simulator.HoldsOvernight;

    /// <summary>The one scan with N walks; null for a plain run.</summary>
    internal SimulatorComparison? Comparison { get; }
}
