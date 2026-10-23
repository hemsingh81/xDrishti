namespace XDrishti.Domain.Checklist;

/// <summary>System-wide numbers of the checks that are not part of an account's risk profile. <c>ResultsWithinDays</c> 3 and the correlation 0.7 are the design (doc 15, doc 16); the volume share is a stated assumption (spec P7-05).</summary>
/// <param name="ResultsWithinDays">Results this many days ahead or fewer fail.</param>
/// <param name="MaxPctOfAverageDailyVolume">Most the ticket's quantity may be of the average daily volume, percent (assumption: 1).</param>
/// <param name="MaxCorrelation">Highest correlation with open trades and approved tickets (doc 15: 0.7).</param>
public sealed record ChecklistLimits(int ResultsWithinDays = 3, double MaxPctOfAverageDailyVolume = 1.0, double MaxCorrelation = 0.70);
