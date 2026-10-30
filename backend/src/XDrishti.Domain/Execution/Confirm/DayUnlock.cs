using XDrishti.Domain.Market;

namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The owner's daily presence proof for one account (spec P12-08 criterion 8, D1): a fresh password once per account and India session date, valid until 15:30 India time of that date. Locking the session or its idle timeout does not end it;
/// the tap still needs the live session. The database stamps the time and refuses an unlock for another day or after the close.
/// </summary>
public static class DayUnlock
{
    public static readonly TimeOnly ValidUntil = new(15, 30);

    public static DateTimeOffset ExpiresAt(DateOnly session) => new(session.ToDateTime(ValidUntil), IndiaTime.Offset);

    /// <summary>True while the unlock of <paramref name="session"/> counts at <paramref name="now"/>.</summary>
    public static bool IsValid(DateOnly session, DateTimeOffset unlockExpiresAt, DateOnly ticketSession, DateTimeOffset now) => session == ticketSession && now < unlockExpiresAt;
}
