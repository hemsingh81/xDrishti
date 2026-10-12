namespace XDrishti.Domain.Market.Calendar;

/// <summary>
/// Whether a calendar day counts. Only <see cref="Confirmed"/> days change what the system treats as a session; a <see cref="Proposed"/> one waits for the
/// owner; a <see cref="Dismissed"/> one is a proposal (or a listed day) the owner declined, kept so that it is not proposed again.
/// </summary>
public enum CalendarStatus
{
    Confirmed,
    Proposed,
    Dismissed,
}

/// <summary>The codes of <see cref="CalendarStatus"/> used in the database and the API: <c>confirmed</c> · <c>proposed</c> · <c>dismissed</c>.</summary>
public static class CalendarStatusCodes
{
    public const string Confirmed = "confirmed";
    public const string Proposed = "proposed";
    public const string Dismissed = "dismissed";

    public static string ToCode(this CalendarStatus status) => status switch
    {
        CalendarStatus.Confirmed => Confirmed,
        CalendarStatus.Proposed => Proposed,
        CalendarStatus.Dismissed => Dismissed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown calendar status."),
    };
}
