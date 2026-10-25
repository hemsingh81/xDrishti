using System.Globalization;

namespace XDrishti.Application.Planning;

/// <summary>The expected failures of the ticket alerts (P7-20) and the notice-id grammar.</summary>
public static class TicketAlertErrors
{
    /// <summary>The session is over or the notice was dismissed: nothing is left to change.</summary>
    public const string AlertClosedCode = "plans.alert_closed";

    /// <summary>A notice was snoozed the most times it may be.</summary>
    public const string SnoozeLimitCode = "plans.snooze_limit";

    /// <summary>Splits <c>crossing:{id}</c> or <c>prompt:{id}</c>; null for anything else.</summary>
    public static (string Kind, long Id)? ParseNotice(string? noticeId)
    {
        var parts = (noticeId ?? string.Empty).Split(':');
        return parts is [var kind and ("crossing" or "prompt"), var number] && long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (kind, id) : null;
    }
}
