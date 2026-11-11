namespace XDrishti.Domain.Replay.Comparison;

/// <summary>The close-of-session equity of one replay on one session.</summary>
public readonly record struct ReplayDayEquity(DateOnly Session, decimal Equity);
