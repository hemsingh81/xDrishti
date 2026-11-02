using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>
/// "How many trading sessions until this company's next results meeting, as known at instant t" (P15-04). A meeting intimated after t never contributes; no entry is <c>unavailable</c> with a closed reason, never zero and never an estimate. Not yet referenced by any
/// decision code (an architecture test): P15-06 names its caller deliberately.
/// </summary>
public interface IResultsCalendar
{
    ValueTask<ResultsAnswer> DaysToResults(Guid instrumentId, DateTimeOffset t, CancellationToken cancellationToken);

    /// <summary>The same for a list of instruments with one query.</summary>
    ValueTask<IReadOnlyDictionary<Guid, ResultsAnswer>> DaysToResults(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset t, CancellationToken cancellationToken);

    /// <summary>Every visible upcoming results meeting of every instrument at t, earliest first.</summary>
    ValueTask<IReadOnlyList<UpcomingMeeting>> UpcomingAsync(DateTimeOffset t, CancellationToken cancellationToken);
}
