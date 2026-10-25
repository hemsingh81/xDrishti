using System.Globalization;

namespace XDrishti.Application.Planning;

/// <summary>The expected failures of the ticket alerts (P7-20) and the notice-id grammar.</summary>
public static class TicketAlertErrors
{
    /// <summary>The session is over or the notice was dismissed: nothing is left to change.</summary>
    public const string AlertClosedCode = "plans.alert_closed";

    /// <summary>A notice was snoozed the most times it may be.</summary>
    public const string SnoozeLimitCode = "plans.snooze_limit";

    /// <summary>The stored kind of an act on the ambiguous form of a prompt: its own notice, apart from the open one (ADR 0093: nothing is guessed).</summary>
    public const string AmbiguousKind = "prompt_ambiguous";

    /// <summary>Splits <c>crossing:{id}</c>, <c>prompt:{id}</c> or <c>prompt:{id}:ambiguous</c> into the stored kind and the row id; null for anything else.</summary>
    public static (string Kind, long Id)? ParseNotice(string? noticeId)
    {
        var parts = (noticeId ?? string.Empty).Split(':');
        var valid = parts is ["crossing" or "prompt", _] or ["prompt", _, "ambiguous"];
        if (!valid || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return null;
        }

        return (parts.Length == 3 ? AmbiguousKind : parts[0], id);
    }
}
