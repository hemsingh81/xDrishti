namespace XDrishti.Domain.Filings;

/// <summary>
/// The answer to "how many trading sessions until the next results meeting, as known at t". <see cref="State"/> is <c>value</c> or <c>unavailable</c> with a closed <see cref="Reason"/>: <c>no_entry</c> (no visible results row), <c>only_cancelled</c> or <c>no_upcoming_entry</c>.
/// Absence is never "far away" and never zero.
/// </summary>
public sealed record ResultsAnswer(string State, int? Days, DateOnly? MeetingDate, DateOnly? EventSession, DateOnly? PeriodEnd, string? Reason)
{
    public const string Value = "value";

    public const string Unavailable = "unavailable";

    public const string NoEntry = "no_entry";

    public const string OnlyCancelled = "only_cancelled";

    public const string NoUpcomingEntry = "no_upcoming_entry";

    public static ResultsAnswer Missing(string reason) => new(Unavailable, null, null, null, null, reason);

    public static ResultsAnswer Of(UpcomingMeeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        return new ResultsAnswer(Value, meeting.Days, meeting.MeetingDate, meeting.EventSession, meeting.PeriodEnd, null);
    }
}
