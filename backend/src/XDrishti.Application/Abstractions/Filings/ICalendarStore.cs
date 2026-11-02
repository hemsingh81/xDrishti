using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>A board-meeting row ready to be stored: its envelope (kind <c>board_meeting</c>, the intimation time as the broadcast time) and the calendar fields.</summary>
public sealed record NewMeeting(NewFiling Filing, DateOnly MeetingDate, string Purpose, string Status);

/// <summary>
/// <c>filings.filings</c> plus <c>filings.calendar</c> (P15-04): one row is one transaction, insert-only. The group of (instrument, period, purpose) is locked, <c>PointInTimeRule.Decide</c> picks the outcome (a later intimation supersedes the stored current row, an equal one is
/// ambiguous, an earlier one is stored unlinked and out of order). Idempotent on the content hash.
/// </summary>
public interface ICalendarStore
{
    ValueTask<FilingInsertResult> InsertAsync(NewMeeting meeting, CancellationToken cancellationToken);
}
