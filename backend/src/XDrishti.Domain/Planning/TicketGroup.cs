namespace XDrishti.Domain.Planning;

/// <summary>Which list of the plan a candidate is on (doc 10 §5). Fixed when the plan is published; only Primary and Reserve tickets can move.</summary>
public enum TicketGroup
{
    /// <summary>Proposed for tomorrow.</summary>
    Primary,

    /// <summary>A stand-by the owner may approve in the evening ("swap with reserve"); never armed on its own.</summary>
    Reserve,

    /// <summary>A candidate the selection turned down, kept with its reasons and graded; not a ticket.</summary>
    Rejected,
}

/// <summary>The spelling of a <see cref="TicketGroup"/> in the tables and the API.</summary>
public static class TicketGroupText
{
    /// <summary>The lower-case code.</summary>
    public static string Code(this TicketGroup group) => group.ToString().ToLowerInvariant();

    /// <summary>Reads a code back; null when unknown.</summary>
    public static TicketGroup? TryParse(string? code) =>
        code is not null && Enum.GetValues<TicketGroup>().Cast<TicketGroup?>().FirstOrDefault(s => string.Equals(s!.Value.Code(), code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : null;
}
