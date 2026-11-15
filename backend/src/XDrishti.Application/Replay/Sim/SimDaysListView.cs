namespace XDrishti.Application.Replay.Sim;

/// <summary>A page of simulations (SR-04), newest first; <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record SimDaysListView(IReadOnlyList<SimDaysListItemView> Items, string? NextCursor);

/// <summary>One simulation in the list: <see cref="Active"/> marks the one active run (idle, settling or settled); <see cref="Equity"/> is the closing equity of the newest stored day, the capital before the first.</summary>
public sealed record SimDaysListItemView(Guid Id, string Phase, bool Active, bool Auto, string Mode, SimDaysScopeView Scope, DateOnly Start, DateOnly End, DateOnly Cursor, decimal Capital, decimal Equity, int DaysDone, DateTimeOffset CreatedAt);

/// <summary>The scope of a simulation as counts: the baskets and the stocks it was started with.</summary>
public sealed record SimDaysScopeView(int Baskets, int Instruments);

/// <summary>A page of a run's stored days (SR-04); <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record SimDayListView(IReadOnlyList<SimDayListItemView> Items, string? NextCursor);

/// <summary>One stored day: the closing equity, the trades entered or exited on it, the picks and whether it rests on a released hold-out block (<c>seen_holdout</c>).</summary>
public sealed record SimDayListItemView(DateOnly Session, decimal EquityClose, int Trades, int Picks, bool SeenHoldOut);

/// <summary>The simulation's defaults (section <c>Sim</c>): the starting capital in rupees, the playback seconds per market minute at 1X and whether a complete run extends by itself.</summary>
public sealed record SimDaysSettingsView(decimal DefaultCapital, int SpeedSecondsPerMinute, bool AutoContinue);
