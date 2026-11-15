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
