namespace XDrishti.Domain.PairResearch;

/// <summary>One session of an instrument's daily series: the date, the last 1-minute close and the turnover (close times volume).</summary>
public sealed record DailyPoint(DateOnly Date, double Close, double Turnover);
