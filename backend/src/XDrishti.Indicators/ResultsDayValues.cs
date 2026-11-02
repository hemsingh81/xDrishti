namespace XDrishti.Indicators;

/// <summary>The two results-event numbers of one India date for one instrument (P15-05), as known at the start of that date; NaN where unknown (never zero, never guessed).</summary>
/// <param name="DaysToResults">Sessions to the next results event.</param>
/// <param name="DaysSinceResults">Sessions since the reaction session of the newest results filing.</param>
/// <param name="SueQuintile">Quintile 1 to 5 of that filing's <c>sue_yoy</c> by the breakpoints of the previous complete season (P15-09), as of the close of the date; NaN when unknown.</param>
/// <param name="Car3Quintile">Quintile 1 to 5 of that filing's <c>results_car3</c> likewise (P15-09).</param>
public readonly record struct ResultsDayValues(double DaysToResults, double DaysSinceResults, double SueQuintile = double.NaN, double Car3Quintile = double.NaN);
