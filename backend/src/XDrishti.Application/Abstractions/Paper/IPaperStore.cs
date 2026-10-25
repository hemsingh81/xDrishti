using XDrishti.Application.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>A stored epoch (<c>paper.epochs</c>): the period a count belongs to. P10-01 has the one default epoch; P10-05 adds more.</summary>
/// <param name="Id">The id.</param>
/// <param name="StartedOn">The first session of the epoch.</param>
/// <param name="StartedAt">When it was recorded.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="Reason">Why.</param>
/// <param name="Actor">Who.</param>
public sealed record EpochRow(long Id, DateOnly StartedOn, DateTimeOffset StartedAt, string Kind, string Reason, string Actor);

/// <summary>A paper account's capital in an epoch (<c>paper.epoch_accounts</c>).</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Capital">The simulated capital.</param>
/// <param name="CapitalSource">Where the figure comes from.</param>
public sealed record EpochAccount(Guid AccountId, decimal Capital, string CapitalSource);

/// <summary>What is stored for one ticket and account.</summary>
/// <param name="EntryId">The entry, or null.</param>
/// <param name="Entry">The stored entry's instant and price, or null.</param>
/// <param name="HasExit">Whether the exit is stored.</param>
/// <param name="Skipped">Whether a skip is stored.</param>
public sealed record StoredPair(long? EntryId, PaperEntry? Entry, bool HasExit, bool Skipped)
{
    /// <summary>Nothing more to write: a skip, or an entry with its exit.</summary>
    public bool IsComplete => Skipped || HasExit;
}

/// <summary>An entry to store, with its exit when the replay has closed the trade.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="MeasureStatus">measured or straddle.</param>
/// <param name="Exit">The exit, or null while open.</param>
public sealed record NewPaperEntry(PaperEntry Entry, string MeasureStatus, PaperExit? Exit);

/// <summary>The exit of an entry that was stored by an earlier run.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="EntryAt">The entry's instant (the database ties the exit to it).</param>
/// <param name="Exit">The exit.</param>
public sealed record NewPaperExit(long EntryId, DateTimeOffset EntryAt, PaperExit Exit);

/// <summary>The row of a session a run processed; the counts of what was written are added by the store.</summary>
/// <param name="SessionDate">The session.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
/// <param name="Outcome">done, partial or failed.</param>
/// <param name="Seen">Armed allocations of the session.</param>
/// <param name="Pending">Pairs left for a later run.</param>
/// <param name="Integrity">Stored entries a later replay disagreed with.</param>
/// <param name="Manual">Whether a person started it.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record NewCaptureRun(DateOnly SessionDate, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, int Seen, int Pending, int Integrity, bool Manual, string Reason);

/// <summary>Everything one session writes, in one transaction.</summary>
/// <param name="EpochId">The epoch.</param>
/// <param name="Accounts">The accounts of the written entries, for the epoch's account rows.</param>
/// <param name="Entries">New entries (with their exits when closed).</param>
/// <param name="Exits">Exits of entries stored earlier.</param>
/// <param name="Skips">New skips.</param>
/// <param name="Run">The session's coverage row.</param>
public sealed record SessionWrite(long EpochId, IReadOnlyList<EpochAccount> Accounts, IReadOnlyList<NewPaperEntry> Entries, IReadOnlyList<NewPaperExit> Exits, IReadOnlyList<PaperSkip> Skips, NewCaptureRun Run);

/// <summary>What a session write stored; a row that was stored already is not counted.</summary>
/// <param name="Entries">Entries written.</param>
/// <param name="Exits">Exits written.</param>
/// <param name="Skips">Skips written.</param>
public sealed record SessionWritten(int Entries, int Exits, int Skips);

/// <summary>The sessions that already have a capture row, and those to look at again.</summary>
/// <param name="Covered">Sessions with at least one capture row.</param>
/// <param name="Revisit">Sessions whose newest row is not done, or with an entry that has no exit.</param>
public sealed record SessionCoverage(IReadOnlySet<DateOnly> Covered, IReadOnlySet<DateOnly> Revisit);

/// <summary>
/// The paper record (<c>paper.*</c>, spec P10-01, ADR 0116) behind a port. Insert-only: the runtime role can read and insert and nothing else. No member places an order or moves a ticket.
/// </summary>
public interface IPaperStore
{
    /// <summary>The newest epoch, or null before the first capture.</summary>
    ValueTask<EpochRow?> CurrentEpochAsync(CancellationToken cancellationToken);

    /// <summary>The newest epoch; the first one (started on <paramref name="startedOn"/>) is stored when there is none.</summary>
    ValueTask<EpochRow> EnsureFirstEpochAsync(DateOnly startedOn, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>What is stored for each (ticket, account) of the plans.</summary>
    ValueTask<IReadOnlyDictionary<(long ItemId, Guid AccountId), StoredPair>> PairsAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken);

    /// <summary>Which sessions have a capture row and which are to be looked at again.</summary>
    ValueTask<SessionCoverage> CoverageAsync(CancellationToken cancellationToken);

    /// <summary>Stores a session's rows in one transaction; a row that exists already is left alone.</summary>
    ValueTask<SessionWritten> WriteSessionAsync(SessionWrite write, CancellationToken cancellationToken);

    /// <summary>Entries newest first, with their exits. Read only.</summary>
    ValueTask<IReadOnlyList<PaperEntryView>> ListEntriesAsync(DateOnly? from, DateOnly? to, Guid? accountId, long? epochId, int limit, CancellationToken cancellationToken);

    /// <summary>Capture rows newest first. Read only.</summary>
    ValueTask<IReadOnlyList<CaptureRunView>> ListRunsAsync(int limit, CancellationToken cancellationToken);
}
