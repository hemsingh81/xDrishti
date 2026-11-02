namespace XDrishti.Simulation.Walking;

/// <summary>
/// The <c>event_exit</c> overlay as the swing walk runs it (P15-09, ADR 0040 amendment): at the close of a session whose <c>days_to_results</c>, as known at the start of that session, is at most <paramref name="DaysBeforeResults"/>, the open position leaves at that close, the same
/// mechanics as <c>MAX_HOLD</c> (X8). A session for which <paramref name="DaysToResultsAt"/> gives NaN (no visible meeting, a cancelled one, a date the series does not hold) never fires: the walk counts it and goes on, because "unknown" is not "no event" and not "an event".
/// </summary>
/// <param name="DaysBeforeResults">The threshold in sessions, 1 to 10.</param>
/// <param name="DaysToResultsAt">The sessions to the next results event of the instrument at the start of a date, or NaN when unknown.</param>
internal sealed record EventExitPlan(int DaysBeforeResults, Func<DateOnly, double> DaysToResultsAt);
