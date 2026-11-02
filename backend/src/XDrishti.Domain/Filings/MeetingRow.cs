namespace XDrishti.Domain.Filings;

/// <summary>One accepted line of a board-meeting csv (P15-04): the intimation is the time the exchange published this row's information, evaluated by the broadcast rule (blank or invalid = unavailable, never guessed).</summary>
public sealed record MeetingRow(int Line, string Symbol, DateOnly PeriodEnd, DateOnly MeetingDate, string Purpose, string Status, BroadcastTime Intimation)
{
    public const string Results = "results";

    public const string Other = "other";

    public const string Scheduled = "scheduled";

    public const string Rescheduled = "rescheduled";

    public const string Cancelled = "cancelled";

    /// <summary>The time as stored: whole microseconds, UTC (null when unavailable).</summary>
    public DateTimeOffset? Time => Intimation.State == BroadcastState.Ok ? NewFiling.ToMicroseconds(Intimation.Instant) : null;
}
