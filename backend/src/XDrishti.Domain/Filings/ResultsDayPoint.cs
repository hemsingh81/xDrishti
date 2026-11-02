namespace XDrishti.Domain.Filings;

/// <summary>The results-event numbers of one India date for one instrument, as known at the start of that date (P15-05; the two quintiles as of its close, P15-09); NaN where unknown, never zero.</summary>
/// <param name="DaysToResults">Sessions to the next results event as known at the start of the date, or NaN.</param>
/// <param name="DaysSinceResults">Sessions since the reaction session of the newest visible filing, or NaN.</param>
/// <param name="SueQuintile">Quintile 1 to 5 of that filing's <c>sue_yoy</c> by the breakpoints of the previous complete season (P15-09), as of the close of the date; NaN when unknown or when no surprise inputs were given.</param>
/// <param name="Car3Quintile">Quintile 1 to 5 of that filing's <c>results_car3</c> likewise (P15-09).</param>
public readonly record struct ResultsDayPoint(double DaysToResults, double DaysSinceResults, double SueQuintile = double.NaN, double Car3Quintile = double.NaN);
