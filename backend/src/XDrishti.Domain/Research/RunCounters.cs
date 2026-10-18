namespace XDrishti.Domain.Research;

/// <summary>
/// The figures of a run: those of <see cref="InstrumentRun"/> summed over the instruments, plus the instruments by skip reason (spec P3-04 Scope 10). Built by <see cref="RunAssembler"/>; a counter that is above 0 in one instrument
/// is above 0 here, and a rule that never fires shows as zero candidates and zero trades next to many window steps.
/// </summary>
/// <param name="Instruments">Instruments of the universe.</param>
/// <param name="InstrumentsRun">Instruments that were replayed (not skipped).</param>
/// <param name="Skipped">Instruments skipped, by reason, in the order of <see cref="SkipReason"/>; reasons with no instrument are left out.</param>
/// <param name="BarsRead">Bars read, summed.</param>
/// <param name="BarsFed">Bars fed, summed.</param>
/// <param name="BarsExcluded">Bars excluded, summed.</param>
/// <param name="BarsOutsideSession">Bars outside the window of a session that ran, summed.</param>
/// <param name="Sessions">Session counts, summed.</param>
/// <param name="DiscontinuitiesFlagged">Flagged days, summed.</param>
/// <param name="Scans">The scanner counters of each side summed over the instruments, Buy then Sell.</param>
/// <param name="Walk">The walker counters, summed (the counters of <c>LimitNotReached</c>, <c>EntriesAtOpen</c>, <c>EntriesAtLimit</c>, <c>AmbiguousMinutes</c>, <c>LateSquareOffs</c>, <c>ZeroVolumeMinutes</c> and <c>RiskUnderFourTicks</c> included).</param>
/// <param name="Fates">The fates, summed.</param>
/// <param name="CandidatesInWindow">Candidates dated in the window, summed.</param>
/// <param name="TradesInWindow">Trades dated in the window, summed.</param>
public sealed record RunCounters(
    int Instruments,
    int InstrumentsRun,
    IReadOnlyList<SkipCount> Skipped,
    long BarsRead,
    long BarsFed,
    long BarsExcluded,
    long BarsOutsideSession,
    SessionCounts Sessions,
    long DiscontinuitiesFlagged,
    IReadOnlyList<ScanCounters> Scans,
    WalkCounters Walk,
    RunFates Fates,
    long CandidatesInWindow,
    long TradesInWindow)
{
    /// <summary>The <c>EndOfData</c> trades dated in the window, apart from the trades.</summary>
    public long OpenAtEnd => Fates.OpenAtEnd;

    /// <summary>The candidate outcomes dropped by a flagged day.</summary>
    public long EntriesSuppressed => Fates.EntriesSuppressed;
}
