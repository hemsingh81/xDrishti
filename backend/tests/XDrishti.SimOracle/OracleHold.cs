namespace XDrishti.SimOracle;

/// <summary>The longest hold in sessions (1 to 5) and the calendar that counts them (null: the default week).</summary>
public sealed record OracleHold(int MaxHoldSessions, OracleCalendar? Calendar);
