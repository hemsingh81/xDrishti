namespace XDrishti.Domain.Filings;

/// <summary>The two results-event numbers of one India date for one instrument, as known at the start of that date (P15-05); NaN where unknown, never zero.</summary>
public readonly record struct ResultsDayPoint(double DaysToResults, double DaysSinceResults);
