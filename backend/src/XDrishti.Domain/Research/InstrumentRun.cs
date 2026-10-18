namespace XDrishti.Domain.Research;

/// <summary>
/// What a replay did with one instrument of a run (spec P3-04 Scope 8, Contract): the data it read, the sessions that ran and those left out and why, the flagged days, the counters of the scanner of each side and of the walker,
/// and the fate of every candidate outcome. One per instrument of the universe, in run order. A skipped instrument that was read keeps its data facts and has no trades; one that has no row has only its id.
/// <para>
/// Identities (checked by the tests on every fixture): <c>BarsRead = BarsFed + BarsExcluded</c> (for an instrument skipped before the scan <see cref="BarsFed"/> is the bars of the sessions that run, which is what would have been fed);
/// <c>BarsExcluded</c> is the bars of the excluded sessions plus <see cref="BarsOutsideSession"/>; <c>Fates.Total</c> equals the sum of the scanner's <c>Candidates</c>. All counters cover the replay from the first session that ran to the
/// last (<see cref="CountersCover"/>); the trades are cut to the window.
/// </para>
/// </summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Symbol">Its symbol; <c>?</c> when the instrument has no row.</param>
/// <param name="Skipped">Why the instrument has no trades; null when it was replayed.</param>
/// <param name="SkipDetail">A note on the skip (for example the reason a tick was refused); null when there is none.</param>
/// <param name="FirstBar">India date of the first bar read; null when none was read.</param>
/// <param name="LastBar">India date of the last bar read.</param>
/// <param name="ReplayFrom">The first session that ran, where the counters start.</param>
/// <param name="CountersCover">The first and last session that ran.</param>
/// <param name="BarsRead">Bars handed to the replay.</param>
/// <param name="BarsFed">Bars of the sessions that ran, inside their windows.</param>
/// <param name="BarsExcluded">Bars of the excluded sessions plus the bars outside the window of a session that ran.</param>
/// <param name="BarsOutsideSession">Bars outside the window of a session that ran (an evening bar on a regular date, a bar at 15:30).</param>
/// <param name="BarsSha256">SHA-256 of the stored bars read (<see cref="BarStreamHash"/>); null when the instrument has no row.</param>
/// <param name="TickSize">The tick in rupees the fills used; null when none was used (a refused tick, or an instrument not found).</param>
/// <param name="TickFromFallback">The tick was not in the instrument master and the NSE default was used.</param>
/// <param name="Adjustments">The applied actions that changed the bars read.</param>
/// <param name="Sessions">The sessions that ran and those excluded, by reason.</param>
/// <param name="Notes">One note per excluded session, in date order, at most <see cref="MaxNotes"/>.</param>
/// <param name="NotesTruncated">There were more excluded sessions than notes.</param>
/// <param name="Discontinuities">The flagged days, in date order.</param>
/// <param name="Scans">The counters of the scanner of each side, summed over the segments, Buy then Sell; empty when the instrument was not scanned.</param>
/// <param name="Walk">The counters of the walker, summed over the segments.</param>
/// <param name="Fates">What became of every candidate outcome.</param>
/// <param name="CandidatesInWindow">Candidates whose decision date is in the window (a rule that fires only before <c>From</c> is visible).</param>
/// <param name="TradesInWindow">Trades dated in the window, <c>EndOfData</c> ones included (<c>Fates.Trades + Fates.OpenAtEnd</c>).</param>
public sealed record InstrumentRun(
    Guid InstrumentId,
    string Symbol,
    SkipReason? Skipped,
    string? SkipDetail,
    DateOnly? FirstBar,
    DateOnly? LastBar,
    DateOnly? ReplayFrom,
    DateSpan? CountersCover,
    long BarsRead,
    long BarsFed,
    long BarsExcluded,
    long BarsOutsideSession,
    string? BarsSha256,
    decimal? TickSize,
    bool TickFromFallback,
    IReadOnlyList<AppliedAdjustment> Adjustments,
    SessionCounts Sessions,
    IReadOnlyList<SessionNote> Notes,
    bool NotesTruncated,
    IReadOnlyList<DiscontinuityNote> Discontinuities,
    IReadOnlyList<ScanCounters> Scans,
    WalkCounters Walk,
    RunFates Fates,
    long CandidatesInWindow,
    long TradesInWindow)
{
    /// <summary>The most session notes a record keeps; the counts are exact beyond it.</summary>
    public const int MaxNotes = 100;

    /// <summary>The record of an instrument id that has no row: skipped as <c>instrument_not_found</c>, symbol <c>?</c>, no data facts (spec P3-04a D3).</summary>
    public static InstrumentRun NotFound(Guid instrumentId) => new(
        instrumentId, "?", SkipReason.InstrumentNotFound, null,
        null, null, null, null,
        0, 0, 0, 0, null,
        null, false,
        [], SessionCounts.Empty, [], false, [],
        [], WalkCounters.Empty, RunFates.Empty,
        0, 0);
}
