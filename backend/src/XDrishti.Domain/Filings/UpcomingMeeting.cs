namespace XDrishti.Domain.Filings;

/// <summary>A visible, not cancelled results meeting whose event session is not past: <see cref="Days"/> trading days from the India date of the question; <see cref="Revision"/> is 1 for the first row of the meeting and 2 and up for each reschedule visible at the question.</summary>
public sealed record UpcomingMeeting(Guid InstrumentId, DateOnly PeriodEnd, DateOnly MeetingDate, DateOnly EventSession, int Days, DateTimeOffset IntimatedAt, int Revision);
