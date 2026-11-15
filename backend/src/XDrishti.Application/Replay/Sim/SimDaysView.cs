using XDrishti.Application.Replay.Reads;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The status of a day-by-day simulation (SR-04): the cursor day, the phase, the equity and how many days are done of the total, with the hindsight labels, the cost basis, the hold-out note and why the runner waits (<c>market_hours</c>, <c>low_disk</c>, <c>data_job</c>, <c>backfill</c>, <c>sim_all</c>), or null.</summary>
public sealed record SimDaysView(
    Guid Id,
    DateOnly Start,
    DateOnly End,
    DateOnly RequestedEnd,
    DateOnly Cursor,
    string Phase,
    bool Auto,
    string Mode,
    decimal Capital,
    decimal Equity,
    decimal? SettledEquity,
    int DaysDone,
    int DaysTotal,
    string? HoldOutNote,
    string? Waiting,
    string? FailReason,
    IReadOnlyList<ReplayLabelView> Hindsight,
    string CostsLabel,
    int SpeedSecondsPerMinute,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
