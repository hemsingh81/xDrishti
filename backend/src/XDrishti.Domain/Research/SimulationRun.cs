namespace XDrishti.Domain.Research;

/// <summary>
/// The record of a simulation run (spec P3-04, Contract; doc 11 §6): enough about the data (the hash of the bars read of every instrument), the strategy (its text and hash, its parameters), the rules (the engine version, the
/// fills, the options) and the calendar for the run to be checked again. It reads no clock and no random source, so equal inputs give an equal record; when and how long belong to the caller. The names of its public properties are pinned by a
/// test: changing one needs <see cref="CurrentSchemaVersion"/> raised (and the run key with it).
/// </summary>
/// <param name="SchemaVersion">4 (3 was the record with swing positions, P3-03d; 2 was the record of P3-03b and P3-04; 1 the one before the fill rules).</param>
/// <param name="RunKey">The lower-case hex SHA-256 of the key text (<see cref="RunAssembler.KeyText"/>): equal for equal inputs, different when anything that can change a trade changes.</param>
/// <param name="EngineVersion"><c>SimulationEngine.Version</c> of the engine that ran.</param>
/// <param name="Strategy">The strategy as recorded.</param>
/// <param name="From">First date of the window (India date).</param>
/// <param name="To">Last date of the window, inclusive.</param>
/// <param name="Options">The thresholds of the data notes.</param>
/// <param name="AcknowledgeCsvAdjusted">The run went on over CSV-adjusted series that also have an applied action.</param>
/// <param name="Fills">The fill rules actually used, as <c>FillRules.ToCanonicalJson()</c>.</param>
/// <param name="Universe">How the instruments were chosen.</param>
/// <param name="CalendarExceptions">The exceptions to the default week from the earliest first bar read to <paramref name="To"/>.</param>
/// <param name="Instruments">One record per instrument of the universe, in run order.</param>
/// <param name="Trades">The trades of all instruments, by entry instant, then place in the universe, then Buy before Sell.</param>
/// <param name="OpenAtEnd">The <c>EndOfData</c> trades, in the same order, apart because statistics must not count them.</param>
/// <param name="Totals">The figures summed over the instruments.</param>
/// <param name="Costs">What the run charged with (ADR 0042, D14); null for a gross run.</param>
public sealed record SimulationRun(
    int SchemaVersion,
    string RunKey,
    string EngineVersion,
    StrategyRecord Strategy,
    DateOnly From,
    DateOnly To,
    ReplayOptions Options,
    bool AcknowledgeCsvAdjusted,
    string Fills,
    UniverseRecord Universe,
    IReadOnlyList<CalendarExceptionDay> CalendarExceptions,
    IReadOnlyList<InstrumentRun> Instruments,
    IReadOnlyList<RunTrade> Trades,
    IReadOnlyList<RunTrade> OpenAtEnd,
    RunCounters Totals,
    CostRecord? Costs = null)
{
    /// <summary>The schema of the record and of the run key this code writes.</summary>
    public const int CurrentSchemaVersion = 4;
}
