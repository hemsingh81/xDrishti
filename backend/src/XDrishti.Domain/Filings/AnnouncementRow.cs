namespace XDrishti.Domain.Filings;

/// <summary>One accepted line of an announcements csv (P15-07): the broadcast time is evaluated by the broadcast rule (blank or invalid = unavailable, never guessed). Category, subject and text are third-party text: stored and exported as data, never interpreted.</summary>
public sealed record AnnouncementRow(int Line, string Symbol, BroadcastTime Broadcast, string Category, string Subject, string Text)
{
    /// <summary>The time as stored: whole microseconds, UTC (null when unavailable).</summary>
    public DateTimeOffset? Time => Broadcast.State == BroadcastState.Ok ? NewFiling.ToMicroseconds(Broadcast.Instant) : null;
}
