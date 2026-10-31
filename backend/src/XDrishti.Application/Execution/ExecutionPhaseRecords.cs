namespace XDrishti.Application.Execution;

public sealed record PhaseAccount(Guid Id, string Name);

/// <summary>A row of the mode trail (insert-only); <see cref="Kind"/> is promotion, demotion or owner_demotion.</summary>
public sealed record PhaseTrailRow(DateTimeOffset EffectiveFrom, string FromMode, string ToMode, string Kind);

/// <summary>A row of the settings record; <see cref="CanaryPending"/> is null when the row does not hold the key (the registry default is pending).</summary>
public sealed record PhaseSettingsRow(DateTimeOffset EffectiveFrom, bool? CanaryPending);

/// <summary>An entry leg that was sent. <see cref="Confirmed"/> is true only with a confirmed outcome by an owner on the intent's ticket at most 30 s before the send began.</summary>
public sealed record PhaseEntryRow(DateOnly SessionDate, string Environment, bool Confirmed);

public sealed record PhaseTicketRow(DateOnly SessionDate, string? Outcome, DateTimeOffset IssuedAt, DateTimeOffset? DecidedAt);

public sealed record PhaseRunRow(DateOnly SessionDate, string Kind, bool Readable);

public sealed record PhaseBreakRow(DateOnly SessionDate, string Kind);

/// <summary>An alarm or a halt: the session date in India and its code (the trigger for a halt).</summary>
public sealed record PhaseCodeRow(DateOnly SessionDate, string Code);

public sealed record PhaseDayStateRow(DateOnly SessionDate, string State);

/// <summary>A live position of <see cref="SessionDate"/> that was not flat at that day's end; <see cref="ClosedOn"/> is the day of the exit that closed it, null while it is open.</summary>
public sealed record PhaseCarriedRow(DateOnly SessionDate, DateOnly? ClosedOn);

public sealed record PhaseDrillRow(DateOnly Date, bool Passed);

/// <summary>Everything one account has in the window, read with SELECT only. Lists are bounded by the window (at most 200 sessions).</summary>
/// <remarks><c>SandboxDates</c> holds the session date of every sandbox intent; those are listed and never counted.</remarks>
public sealed record PhaseAccountRecords(
    IReadOnlyList<PhaseEntryRow> Entries, IReadOnlyList<PhaseTicketRow> Tickets, IReadOnlyList<PhaseRunRow> Runs, IReadOnlyList<PhaseBreakRow> Breaks, IReadOnlyList<PhaseCodeRow> Alarms,
    IReadOnlyList<PhaseCodeRow> Halts, IReadOnlyList<PhaseDayStateRow> DayStates, IReadOnlyList<PhaseCarriedRow> Carried, IReadOnlyList<PhaseDrillRow> Drills, int LiveLegs, int LiveLegsWithAlgoId, int LiveEntryFills,
    IReadOnlyList<DateOnly> SandboxDates);


/// <summary>The stored records the P12 phase check judges (spec P12-12). Read only: the adapter has SELECT and nothing else.</summary>
public interface IExecutionRecordsReader
{
    /// <summary>The accounts with a Trading role (one when <paramref name="accountId"/> is given and found), ordered by name.</summary>
    ValueTask<IReadOnlyList<PhaseAccount>> AccountsAsync(Guid? accountId, CancellationToken cancellationToken);

    /// <summary>The whole mode trail of an account, oldest first.</summary>
    ValueTask<IReadOnlyList<PhaseTrailRow>> TrailAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The settings rows, oldest first.</summary>
    ValueTask<IReadOnlyList<PhaseSettingsRow>> SettingsAsync(CancellationToken cancellationToken);

    /// <summary>The newest session date any execution record carries, null on an empty database.</summary>
    ValueTask<DateOnly?> NewestSessionAsync(CancellationToken cancellationToken);

    /// <summary>The records of one account for the sessions <paramref name="from"/> to <paramref name="through"/>, plus the positions carried into the window.</summary>
    ValueTask<PhaseAccountRecords> RecordsAsync(Guid accountId, DateOnly from, DateOnly through, CancellationToken cancellationToken);
}
