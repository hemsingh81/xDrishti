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

/// <summary>What the tape of one day reads (SR-04 part B), and nothing more of the run: the stored day's record and trades (null when the day is not stored), the closing equity of the stored day before it (null on the first day) and the trades still open coming into the day (their latest stored state, a JSON array).</summary>
public sealed record SimTapeDay(string? RecordJson, string? TradesJson, decimal? EquityOpen, string HeldJson);

/// <summary>One simulation in the list (SR-04): the header columns and the scope's counts, never the stored texts. <paramref name="LastEquity"/> is the closing equity of the newest stored day, null before the first.</summary>
public sealed record SimDaysListRow(Guid Id, string Phase, bool Auto, string Mode, int Baskets, int Instruments, DateOnly Start, DateOnly End, DateOnly Cursor, decimal Capital, decimal? LastEquity, int DaysDone, DateTimeOffset CreatedAt);

/// <summary>One stored day in a run's list of days (SR-04): a projection, never the record text. <paramref name="SeenHoldOut"/>: the day's labels carry a <c>seen_holdout:</c> label.</summary>
public sealed record SimDayListRow(DateOnly Session, decimal EquityClose, int Trades, int Picks, bool SeenHoldOut);
