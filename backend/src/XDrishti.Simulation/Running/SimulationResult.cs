using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Running;

/// <summary>
/// What one <see cref="Simulator.Run"/> gave for one instrument: every candidate with its walk (<see cref="Outcomes"/>), the trades, the positions still open when the data ended, the counters of the scanner of each side and
/// the counters of the walker, under a header that says what the result is. The result types live here, in <c>Running</c>, because <see cref="CandidateOutcome"/> holds a type of <c>Walking</c> and the root namespace
/// depends on neither stage.
/// <para>
/// <see cref="Outcomes"/> is ordered by decision instant, then Buy before Sell. <see cref="Trades"/> are the trades that did not end because the data did, ordered by entry instant, then Buy before Sell;
/// <see cref="OpenAtEnd"/> are the <c>EndOfData</c> trades in the same order, kept apart because a position cut short by the end of the list is data, not a rule, and statistics must not count it.
/// </para>
/// </summary>
/// <param name="Header">What the result says it is.</param>
/// <param name="Outcomes">Every candidate with the outcome of its walk, trades and no-trades alike.</param>
/// <param name="Scans">The counters of the scanner of each listed side, Buy then Sell.</param>
/// <param name="Walk">The walker's counters: the sum of those of the outcomes.</param>
public sealed record SimulationResult(SimulationHeader Header, IReadOnlyList<CandidateOutcome> Outcomes, IReadOnlyList<ScanCounters> Scans, WalkCounters Walk)
{
    /// <summary>The trades that ended at the stop, the target or the square-off, by entry instant, then Buy before Sell.</summary>
    public IReadOnlyList<SimTrade> Trades { get; } = Pick(Outcomes, endOfData: false);

    /// <summary>The trades that ended because the data did (<c>EndOfData</c>), in the same order.</summary>
    public IReadOnlyList<SimTrade> OpenAtEnd { get; } = Pick(Outcomes, endOfData: true);

    private static System.Collections.ObjectModel.ReadOnlyCollection<SimTrade> Pick(IReadOnlyList<CandidateOutcome> outcomes, bool endOfData) =>
        outcomes
            .Select(o => o.Walk.Trade)
            .OfType<SimTrade>()
            .Where(t => (t.ExitReason == ExitReason.EndOfData) == endOfData)
            .OrderBy(t => t.EntryEpochSeconds)
            .ThenBy(t => t.Side)
            .ToList()
            .AsReadOnly();
}
