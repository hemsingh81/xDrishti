namespace XDrishti.Indicators;

/// <summary>The two results-event numbers of one India date for one instrument (P15-05), as known at the start of that date; NaN where unknown (never zero, never guessed).</summary>
/// <param name="DaysToResults">Sessions to the next results event.</param>
/// <param name="DaysSinceResults">Sessions since the reaction session of the newest results filing.</param>
public readonly record struct ResultsDayValues(double DaysToResults, double DaysSinceResults);
