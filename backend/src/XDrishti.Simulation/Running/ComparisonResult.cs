using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Running;

/// <summary>
/// What one <see cref="SimulatorComparison.Run"/> gave for one instrument (P4-05, D2): the result of each candidate, in the order of the candidates, and the counters of the one scan they share. Every member's <see cref="SimulationResult.Scans"/>
/// is the same list as <see cref="Scans"/>: the scan ran once. The walks are not gated (D4).
/// </summary>
/// <param name="Members">One result per candidate, each as <see cref="Simulator.Run"/> of that candidate alone would give it, except that a swing side is not gated.</param>
/// <param name="Scans">The counters of the scanner of each side, Buy then Sell.</param>
public sealed record ComparisonResult(IReadOnlyList<SimulationResult> Members, IReadOnlyList<ScanCounters> Scans)
{
    /// <summary>
    /// How many of the entries of a member the position gate of a plain run would have skipped (D4): the gate applied to each side's walks in decision order, counting those <paramref name="counted"/> accepts. Zero for an intraday strategy, whose
    /// trades leave on their entry date, which the caller does not ask about.
    /// </summary>
    /// <param name="member">The index of the candidate.</param>
    /// <param name="counted">Which of the blocked entries to count (the replay counts those decided in its window).</param>
    public long BlockedByGate(int member, Func<EntryCandidate, bool> counted) => PositionGate.CountBlocked(Members[member].Outcomes, counted);
}
