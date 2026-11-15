namespace XDrishti.Application.Abstractions.Replay;

/// <summary>
/// <c>research.sim_days_runs</c> and <c>research.sim_days_snapshots</c> (SR-04, ADR 0293): the only tables the day-by-day simulation writes. Snapshots are insert-only; a run changes only its cursor, phase, auto flag, end, pending day, hold-out note and failure. Every move is a compare-and-set on the phase (and the cursor), so a
/// repeat or a race changes nothing twice. An active run is one in <c>idle</c>, <c>settling</c> or <c>settled</c>; at most one exists.
/// </summary>
public interface ISimDaysStore
{
    /// <summary>Stores a new run unless another is active (false then); under a lock, so two starts cannot both see none.</summary>
    ValueTask<bool> InsertAsync(NewSimDaysRun run, CancellationToken cancellationToken);

    ValueTask<SimDaysRunRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The active run, or null.</summary>
    ValueTask<SimDaysRunRow?> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>The newest run of any phase, or null.</summary>
    ValueTask<SimDaysRunRow?> LatestAsync(CancellationToken cancellationToken);

    /// <summary>Moves the phase from <paramref name="from"/> to <paramref name="to"/>; false when the run is not in <paramref name="from"/>.</summary>
    ValueTask<bool> MovePhaseAsync(Guid id, string from, string to, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary><c>idle</c> to <c>settling</c>, marked as the owner's manual request or auto-run's; false when the run is not idle.</summary>
    ValueTask<bool> RequestSettleAsync(Guid id, bool manual, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Records why the runner waits on the run (a yield rule), or clears it with null.</summary>
    ValueTask SetWaitingAsync(Guid id, string? reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Sets the hold-out note of an active run (the start writes what its claim of block 1's look did, after the run is stored).</summary>
    ValueTask SetHoldOutNoteAsync(Guid id, string? note, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Sets the auto flag of an active run; false when it is not active.</summary>
    ValueTask<bool> SetAutoAsync(Guid id, bool on, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary><c>settling</c> to <c>settled</c> with the pending day, when the cursor is still <see cref="SimDayPending.Session"/>.</summary>
    ValueTask<bool> SettleAsync(Guid id, SimDayPending pending, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>In one transaction: inserts the snapshot of the settled cursor day and moves the cursor to <paramref name="next"/> (<c>idle</c>) or, with none, marks the run <c>complete</c>. False when the run is not settled on <see cref="SimDaySnapshotRow.Session"/>.</summary>
    ValueTask<bool> AdvanceAsync(Guid id, SimDaySnapshotRow snapshot, DateOnly? next, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>A <c>complete</c> run goes on: the end moves to <paramref name="end"/>, the cursor to <paramref name="cursor"/>, the phase to <c>idle</c>. False when it is not complete or another run is active.</summary>
    ValueTask<bool> ExtendAsync(Guid id, DateOnly end, DateOnly requestedEnd, DateOnly cursor, DateOnly? readThrough, string? holdOutNote, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends an active run as <c>error</c> with the reason.</summary>
    ValueTask<bool> FailAsync(Guid id, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Ends a run that is not cancelled or in error as <c>cancelled</c>; false otherwise.</summary>
    ValueTask<bool> CancelAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The stored days in session order.</summary>
    ValueTask<IReadOnlyList<SimDaySnapshotRow>> SnapshotsAsync(Guid id, CancellationToken cancellationToken);

    ValueTask<SimDaySnapshotRow?> SnapshotAsync(Guid id, DateOnly session, CancellationToken cancellationToken);
}
