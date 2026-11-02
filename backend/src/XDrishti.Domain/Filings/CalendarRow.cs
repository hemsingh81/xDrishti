namespace XDrishti.Domain.Filings;

/// <summary>One stored board-meeting row: its envelope (with the intimation time as <c>BroadcastTs</c>) and the calendar fields.</summary>
public sealed record CalendarRow(StoredFiling Filing, DateOnly MeetingDate, string Purpose, string Status)
{
    public bool IsCancelled => Status == MeetingRow.Cancelled;
}
