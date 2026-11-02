namespace XDrishti.Domain.Filings;

/// <summary>The envelope facts of a stored <c>results</c> filing that the loss tag needs: the instrument, the period it reports (null when it names none) and the exchange broadcast time (whole microseconds). Only filings with a known time (state ok) are given.</summary>
public sealed record ResultsFilingTime(Guid InstrumentId, DateOnly? PeriodEnd, DateTimeOffset BroadcastTs);
