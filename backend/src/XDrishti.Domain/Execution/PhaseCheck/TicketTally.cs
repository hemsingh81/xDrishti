namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The confirm tickets of a session by outcome; <see cref="MedianMinutes"/> is the median from issue to tap and null (insufficient) below <see cref="MinForMedian"/> tickets.</summary>
public sealed record TicketTally(int Issued, int Confirmed, int Skipped, int Expired, int Refused, double? MedianMinutes)
{
    public const int MinForMedian = 5;

    public static TicketTally None { get; } = new(0, 0, 0, 0, 0, null);
}
