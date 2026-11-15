namespace XDrishti.Application.Abstractions.Replay;

/// <summary>A day-by-day simulation to store (SR-04): what it was started with, its cursor on the first session and phase <c>idle</c>.</summary>
public sealed record NewSimDaysRun(
    Guid Id, string InputHash, string ParamsJson, string SettingsJson, string VersionsJson, string? LearningSources, decimal Capital, string Mode, DateOnly Start, DateOnly End, DateOnly RequestedEnd, DateOnly Cursor, DateOnly? ReadThrough, string? HoldOutNote, DateTimeOffset CreatedAt);

/// <summary>A stored day-by-day simulation (SR-04). <paramref name="Cursor"/> is the day to settle next (the last day once complete); the pending day is the settled, not yet advanced one. <paramref name="Manual"/>: the settle in flight was asked by the owner (it runs in market hours too); <paramref name="Waiting"/>: why the runner waits (a yield rule), or null.</summary>
public sealed record SimDaysRunRow(
    Guid Id, string InputHash, string ParamsJson, string SettingsJson, string VersionsJson, string? LearningSources, decimal Capital, string Mode, DateOnly Start, DateOnly End, DateOnly RequestedEnd, DateOnly Cursor, string Phase, bool Auto,
    DateOnly? ReadThrough, string? HoldOutNote, string? FailReason, SimDayPending? Pending, int DaysDone, decimal? LastEquity, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Manual = false, string? Waiting = null);

/// <summary>The settled cursor day, kept on the run until it is advanced: the session record, the trades of the day, the closing equity, the result hash and the end in force when it was settled.</summary>
public sealed record SimDayPending(DateOnly Session, string RecordJson, string TradesJson, decimal EquityClose, string ResultHash, DateOnly EndInForce);

/// <summary>One stored, immutable day of a simulation.</summary>
public sealed record SimDaySnapshotRow(DateOnly Session, string RecordJson, string PicksJson, string BlockedJson, string TradesJson, decimal EquityClose, string ResultHash, DateOnly EndInForce);
