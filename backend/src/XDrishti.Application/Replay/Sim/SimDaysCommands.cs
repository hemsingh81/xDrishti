using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Starts a day-by-day simulation (SR-04, AC 1). <paramref name="End"/> null is the last complete stored session; <paramref name="Capital"/> null is <c>Sim:DefaultCapital</c>; <paramref name="Mode"/> null is <c>learning</c>; <paramref name="RiskProfileId"/> null is the seeded standard profile.</summary>
public sealed record StartSimDaysCommand(DateOnly Start, DateOnly? End, IReadOnlyList<Guid>? BasketIds, IReadOnlyList<Guid>? InstrumentIds, decimal? Capital, Guid? RiskProfileId, string? Mode) : ICommand;

/// <summary>Settles the cursor day (Instant): asks the runner, which moves it to <c>settled</c>.</summary>
public sealed record SettleSimDayCommand(Guid Id) : ICommand;

/// <summary>Stores the settled day and moves the cursor to the next session (AC 7).</summary>
public sealed record AdvanceSimDayCommand(Guid Id) : ICommand;

/// <summary>Turns auto-run on or off (AC 8).</summary>
public sealed record SetSimAutoCommand(Guid Id, bool On) : ICommand;

/// <summary>Extends a complete run to the newest complete stored session (AC 10).</summary>
public sealed record ContinueSimDaysCommand(Guid Id) : ICommand;

/// <summary>Cancels a run.</summary>
public sealed record CancelSimDaysCommand(Guid Id) : ICommand;

/// <summary>The status of one run.</summary>
public sealed record GetSimDaysQuery(Guid Id) : IQuery<Result<SimDaysView>>;

/// <summary>One stored day of a run, or the settled cursor day before it is advanced.</summary>
public sealed record GetSimDayQuery(Guid Id, DateOnly Session) : IQuery<Result<SimDaySnapshotView>>;

/// <summary>One page of a settled day's tape: the minutes from <paramref name="From"/> (default 09:15 India time) to <paramref name="To"/> (default 15:29), both on the day.</summary>
public sealed record GetSimDayTapeQuery(Guid Id, DateOnly Session, DateTimeOffset? From, DateTimeOffset? To) : IQuery<Result<SimDayTapeView>>;

/// <summary>The simulations, newest first, a page at a time: <paramref name="Limit"/> 1 to 100 (default 20), <paramref name="Cursor"/> from the previous page.</summary>
public sealed record ListSimDaysQuery(string? Limit, string? Cursor) : IQuery<Result<SimDaysListView>>;

/// <summary>The stored days of one run, a page at a time; <paramref name="Sort"/> <c>asc</c> (default, oldest first) or <c>desc</c>.</summary>
public sealed record ListSimDayDaysQuery(Guid Id, string? Limit, string? Cursor, string? Sort) : IQuery<Result<SimDayListView>>;

/// <summary>The defaults the start form and the playback read (Sim:DefaultCapital, Sim:DefaultSpeedSecondsPerMinute, Sim:AutoContinue).</summary>
public sealed record GetSimDaysSettingsQuery : IQuery<SimDaysSettingsView>;
